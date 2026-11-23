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

internal static class HouseAchievementClaim
{
	private const uint QuestGid = 7u;

	private static Dictionary<int, JsonObject>? _rows;

	private static Dictionary<int, JsonObject> Load()
	{
		if (_rows != null)
		{
			return _rows;
		}
		_rows = new Dictionary<int, JsonObject>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return _rows;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return _rows;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					int num = Num(jsonObject["ID"]);
					if (num > 0)
					{
						_rows[num] = jsonObject;
					}
				}
			}
		}
		catch
		{
		}
		return _rows;
	}

	public static async Task<JsonArray> ClaimAsync(Connection connection, int id, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return granted;
		}
		Dictionary<int, JsonObject> rows = Load();
		if (!rows.TryGetValue(id, out JsonObject row))
		{
			return granted;
		}
		int check = Math.Max(1, Num(row["CheckValue"]));
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == (uint)id);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 7u,
				Sid = (uint)id,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		if ((attr.Val & 1) != 0)
		{
			return granted;
		}
		if ((int)(attr.Val >> 1) < check)
		{
			return granted;
		}
		JsonNode jsonNode = row["Rewards"];
		if (jsonNode is JsonArray rewards)
		{
			foreach (JsonNode node in rewards)
			{
				JsonArray arr = node as JsonArray;
				if (arr == null || arr.Count < 5)
				{
					continue;
				}
				List<uint> reward = new List<uint>();
				foreach (JsonNode item in arr)
				{
					reward.Add(uint.TryParse(item?.ToString(), out var n) ? n : 0u);
				}
				if (reward.Count < 5 || reward[0] == 0 || reward[4] == 0)
				{
					continue;
				}
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
				JsonArray json = new JsonArray();
				foreach (uint v in reward)
				{
					json.Add(v);
				}
				granted.Add(json);
			}
		}
		attr.Val |= 1u;
		sync.Custom[player.ToPackedAttrKey(7u, (uint)id)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(7u, (uint)id)] = attr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		return granted;
	}

	public static async Task<JsonArray> ClaimAllAsync(Connection connection, IEnumerable<int> ids, NtfSyncPlayer sync)
	{
		JsonArray all = new JsonArray();
		foreach (int id in ids)
		{
			foreach (JsonNode item in await ClaimAsync(connection, id, sync))
			{
				all.Add(item?.DeepClone());
			}
		}
		return all;
	}

	public static List<int> HouseAchievementIds()
	{
		List<int> list = new List<int>();
		foreach (var (item, jsonObject2) in Load())
		{
			int num2 = Num(jsonObject2["Type"]);
			if ((num2 == 264 || num2 == 315 || num2 == 318) ? true : false)
			{
				list.Add(item);
			}
		}
		return list;
	}

	public static Task MarkCompleteAsync(Connection connection, int id, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return Task.CompletedTask;
		}
		Dictionary<int, JsonObject> dictionary = Load();
		if (!dictionary.TryGetValue(id, out var value))
		{
			return Task.CompletedTask;
		}
		int num = Math.Max(1, Num(value["CheckValue"]));
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == (uint)id);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 7u,
				Sid = (uint)id,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val = (playerAttr.Val & 1) | (uint)(num << 1);
		sync.Custom[player.ToPackedAttrKey(7u, (uint)id)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(7u, (uint)id)] = playerAttr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.CompletedTask;
	}

	private static int Num(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
