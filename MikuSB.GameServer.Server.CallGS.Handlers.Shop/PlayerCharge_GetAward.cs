using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("PlayerCharge_GetAward")]
public class PlayerCharge_GetAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint charged = player.GetAttr(0u, 18u);
		uint claimed = player.GetAttr(0u, 19u);
		JsonArray rewards = new JsonArray();
		foreach (JsonObject row in LoadRewards())
		{
			int index = row["INDEX"]?.GetValue<int>() ?? 0;
			uint need = (uint)(row["NUM"]?.GetValue<int>() ?? 0);
			if (index <= 0 || need == 0 || charged < need)
			{
				continue;
			}
			uint bit = (uint)(1 << index);
			if ((claimed & bit) != 0)
			{
				continue;
			}
			JsonArray items = row["ITEM"]?.AsArray();
			if (items == null)
			{
				continue;
			}
			foreach (JsonNode item in items)
			{
				JsonArray arr = item as JsonArray;
				if (arr != null && arr.Count >= 4)
				{
					List<uint> reward = arr.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).ToList();
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
					rewards.Add(new JsonArray(((IEnumerable<uint>)reward).Select((Func<uint, JsonNode>)((uint x) => JsonValue.Create((int)x))).ToArray()));
				}
			}
			claimed |= bit;
		}
		player.SetAttr(0u, 19u, claimed);
		sync.Custom[player.ToPackedAttrKey(0u, 19u)] = claimed;
		sync.Custom[player.ToShiftedAttrKey(0u, 19u)] = claimed;
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		JsonObject response = new JsonObject { ["tbRewards"] = rewards };
		await CallGSRouter.SendScript(connection, "PlayerCharge_GetAward", response.ToJsonString(), sync);
	}

	private static List<JsonObject> LoadRewards()
	{
		List<JsonObject> list = new List<JsonObject>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "activity";
			buffer[3] = "player_charge";
			buffer[4] = "player_charge.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return list;
			}
			JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(path));
			JsonArray jsonArray = (jsonNode as JsonArray) ?? (jsonNode?["value"] as JsonArray);
			if (jsonArray == null)
			{
				return list;
			}
			foreach (JsonNode item2 in jsonArray)
			{
				if (item2 is JsonObject item)
				{
					list.Add(item);
				}
			}
		}
		catch
		{
		}
		return list;
	}
}
