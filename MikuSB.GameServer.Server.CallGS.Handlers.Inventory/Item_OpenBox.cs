using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_OpenBox")]
public class Item_OpenBox : ICallGSHandler
{
	private sealed class BoxRow
	{
		public uint Index { get; set; }

		public string ObtainType { get; set; } = "";

		public List<uint> Gdpln { get; set; } = new List<uint>();
	}

	private static Dictionary<uint, List<BoxRow>>? _boxes;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		ItemOpenBoxParam req = JsonSerializer.Deserialize<ItemOpenBoxParam>(param);
		PlayerInstance player = connection.Player;
		if (req == null || req.Id == 0 || req.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Item_OpenBox", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		InventoryData inv = player.InventoryManager.InventoryData;
		if (!inv.Items.TryGetValue(req.Id, out BaseGameItemInfo box) || box.ItemCount < req.Count)
		{
			await CallGSRouter.SendScript(connection, "Item_OpenBox", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (box.ItemType != ItemTypeEnum.TYPE_USEABLE || !GameData.OtherItemByFullId.TryGetValue(box.TemplateId, out OtherItemExcel other) || other.LuaType != "itembox")
		{
			await CallGSRouter.SendScript(connection, "Item_OpenBox", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		Dictionary<uint, List<BoxRow>> boxes = _boxes ?? (_boxes = LoadBoxes());
		if (!boxes.TryGetValue(other.Param1, out List<BoxRow> rows) || rows.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Item_OpenBox", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		List<ItemSelectParam> list = req.Select;
		List<List<uint>> rewards;
		if (list != null && list.Count > 0)
		{
			ItemSelectParam selected = req.Select[0];
			BoxRow match = rows.FirstOrDefault((BoxRow boxRow) => boxRow.Gdpln.Count >= 5 && selected.Gdpln.Count >= 5 && boxRow.Gdpln.Take(5).SequenceEqual(selected.Gdpln.Take(5)));
			if (match == null)
			{
				await CallGSRouter.SendScript(connection, "Item_OpenBox", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			rewards = new List<List<uint>>(1) { match.Gdpln };
		}
		else
		{
			if (rows.Any((BoxRow boxRow) => string.Equals(boxRow.ObtainType, "Select", StringComparison.OrdinalIgnoreCase)))
			{
				await CallGSRouter.SendScript(connection, "Item_OpenBox", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			rewards = rows.Select((BoxRow boxRow) => boxRow.Gdpln).ToList();
		}
		uint boxUniqueId = box.UniqueId;
		uint maxItemId = ((inv.Items.Count != 0) ? inv.Items.Keys.Max() : 0u);
		if (inv.NextUniqueUid <= maxItemId)
		{
			inv.NextUniqueUid = maxItemId + 1;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		box.ItemCount -= req.Count;
		sync.Items.Add(box.ToProto());
		JsonArray awards = new JsonArray();
		foreach (List<uint> reward in rewards)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			JsonArray row = new JsonArray();
			foreach (uint value in reward)
			{
				row.Add((int)value);
			}
			awards.Add(row);
		}
		if (box.ItemCount == 0)
		{
			inv.Items.Remove(boxUniqueId);
		}
		DatabaseHelper.SaveDatabaseType(inv);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject rsp = new JsonObject { ["tbAward"] = awards };
		await CallGSRouter.SendScript(connection, "Item_OpenBox", rsp.ToJsonString(), sync);
	}

	private static Dictionary<uint, List<BoxRow>> LoadBoxes()
	{
		Dictionary<uint, List<BoxRow>> dictionary = new Dictionary<uint, List<BoxRow>>();
		string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "item", "box.json");
		if (!File.Exists(path))
		{
			return dictionary;
		}
		JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
		if (jsonArray == null)
		{
			return dictionary;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonObject jsonObject))
			{
				continue;
			}
			uint num = jsonObject["Index"]?.GetValue<uint>() ?? 0;
			JsonArray jsonArray2 = jsonObject["GDPLN"] as JsonArray;
			if (num != 0 && jsonArray2 != null && jsonArray2.Count >= 5)
			{
				List<uint> gdpln = jsonArray2.Select((JsonNode x) => x?.GetValue<uint>() ?? 0).ToList();
				if (!dictionary.TryGetValue(num, out var value))
				{
					value = (dictionary[num] = new List<BoxRow>());
				}
				value.Add(new BoxRow
				{
					Index = num,
					ObtainType = (jsonObject["ObtainType"]?.GetValue<string>() ?? ""),
					Gdpln = gdpln
				});
			}
		}
		return dictionary;
	}
}
