using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseLevelRewardHelper
{
	public static async Task<(JsonArray Rewards, NtfSyncPlayer Sync)> GrantAsync(Connection connection, List<uint> reward, uint sid, int bit)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray attrs = new JsonArray();
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		if (((attr.Val >> bit) & 1) == 0)
		{
			attr.Val |= (uint)(1 << bit);
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
			JsonNode[] array = reward.Select((uint x) => JsonValue.Create(x)).ToArray();
			attrs.Add(new JsonArray((ReadOnlySpan<JsonNode?>)array));
		}
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = attr.Val;
		return (Rewards: attrs, Sync: sync);
	}

	public static List<uint>? LoadRow(string file, int level)
	{
		try
		{
			// 2026-11-13 改（修复记录第 271 节）：两个坑。
			// 1) machine 相关配置在 Resources/house/massage/ 子目录，原来只拼 Resources/house/<file>，
			//    所以 massage_level_reward.json 永远读不到 → reward=null → 点了没反应。
			// 2) 配置里的奖励字段是 "Item"（[[16,2,1,1,230]]），原代码找 "Rows"，永远匹配不上。
			//    这里两条都兼容，根目录优先、子目录兜底。
			string root = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house");
			JsonArray? jsonArray = null;
			foreach (string candidate in new string[2]
			{
				Path.Combine(root, file),
				Path.Combine(root, "massage", file)
			})
			{
				if (!File.Exists(candidate))
				{
					continue;
				}
				jsonArray = JsonNode.Parse(File.ReadAllText(candidate))?.AsArray();
				if (jsonArray != null)
				{
					break;
				}
			}
			if (jsonArray == null)
			{
				return null;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || HouseJson.NumField(jsonObject, "Level") != level)
				{
					continue;
				}
				JsonNode node = jsonObject["Item"] ?? jsonObject["Rows"];
				if (!(node is JsonArray array) || array.Count == 0)
				{
					return null;
				}
				JsonArray source = array[0] as JsonArray ?? array;
				return source.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).ToList();
			}
		}
		catch
		{
		}
		return null;
	}
}
