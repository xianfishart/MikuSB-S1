using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_UpBySpecialBreak")]
public class GirlCard_UpBySpecialBreak : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		GirlCardUpBySpecialBreakParam req = JsonSerializer.Deserialize<GirlCardUpBySpecialBreakParam>(param);
		if (req == null || req.CardId == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CardExcel cardTemplate = GameData.CardData.Values.FirstOrDefault((CardExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == card.TemplateId);
		if (cardTemplate == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (cardTemplate.BreakMatID <= 10000 || !GameData.SpecialBreakData.TryGetValue(cardTemplate.BreakMatID, out SpecialBreakExcel specialBreakExcel))
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint nextBreak = card.Break + 1;
		if (!specialBreakExcel.HasBreakLevel(nextBreak))
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"tip.already_max_break\"}");
			return;
		}
		Dictionary<ulong, uint> requestedMaterials = new Dictionary<ulong, uint>();
		foreach (List<int> row in specialBreakExcel.GetItems(nextBreak))
		{
			if (row.Count >= 5)
			{
				ulong templateId = GameResourceTemplateId.FromGdpl((uint)Math.Max(0, row[0]), (uint)Math.Max(0, row[1]), (uint)Math.Max(0, row[2]), (uint)Math.Max(0, row[3]));
				uint count = (uint)Math.Max(0, row[4]);
				if (templateId != 0L && count != 0)
				{
					requestedMaterials[templateId] = requestedMaterials.GetValueOrDefault(templateId) + count;
				}
			}
		}
		if (requestedMaterials.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"tip.not_material_for_break\"}");
			return;
		}
		ulong key;
		uint value;
		foreach (KeyValuePair<ulong, uint> item3 in requestedMaterials)
		{
			item3.Deconstruct(out key, out value);
			ulong templateId2 = key;
			uint count2 = value;
			BaseGameItemInfo item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId2);
			if (item == null || item.ItemCount < count2)
			{
				await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{\"sErr\":\"tip.not_material_for_break\"}");
				return;
			}
		}
		List<Item> syncItems = new List<Item>();
		foreach (KeyValuePair<ulong, uint> item4 in requestedMaterials)
		{
			item4.Deconstruct(out key, out value);
			ulong templateId3 = key;
			uint count3 = value;
			BaseGameItemInfo item2 = player.InventoryManager.InventoryData.Items.Values.First((BaseGameItemInfo x) => x.TemplateId == templateId3);
			item2.ItemCount -= count3;
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				syncItems.Add(BuildRemovedProto(item2));
			}
			else
			{
				syncItems.Add(item2.ToProto());
			}
		}
		card.Break = nextBreak;
		syncItems.Add(card.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		await CallGSRouter.SendScript(connection, "GirlCard_UpBySpecialBreak", "{}", sync);
	}

	private static Item BuildRemovedProto(BaseGameItemInfo item)
	{
		Item item2 = item.ToProto();
		item2.Count = 0u;
		return item2;
	}
}
