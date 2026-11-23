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

namespace MikuSB.GameServer.Game.GachaTry;

public static class GachaTryService
{
	private sealed record TrialInfo(List<int[]> Rewards);

	private const uint ActivityGid = 50u;

	private const uint ClaimSidOffset = 1u;

	private static JsonObject? _config;

	public static async Task<(JsonObject Response, NtfSyncPlayer Sync)> Settle(PlayerInstance player, int actId, int girlIdx)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		TrialInfo trial = FindTrial(actId, girlIdx);
		JsonArray awards = new JsonArray();
		if (trial == null)
		{
			return (Response: new JsonObject
			{
				["tbAwardList"] = awards,
				["tbAward"] = awards
			}, Sync: sync);
		}
		foreach (int[] reward in trial.Rewards)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward.Select((int x) => (uint)x).ToList());
			JsonArray row = new JsonArray();
			int[] array = reward;
			foreach (int value in array)
			{
				row.Add(value);
			}
			awards.Add(row);
		}
		MarkClaimed(player, actId, girlIdx, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		return (Response: new JsonObject
		{
			["nActId"] = actId,
			["nGirlIdx"] = girlIdx,
			["tbAwardList"] = awards,
			["tbAward"] = awards.DeepClone()
		}, Sync: sync);
	}

	public static JsonObject BuildPassedAwards(PlayerInstance player, int actId)
	{
		JsonArray jsonArray = new JsonArray();
		if (LoadConfig()?[actId.ToString()] is JsonArray jsonArray2)
		{
			foreach (JsonNode item in jsonArray2)
			{
				if (!(item is JsonObject jsonObject))
				{
					continue;
				}
				int num = jsonObject["idx"]?.GetValue<int>() ?? 0;
				if (!IsClaimed(player, actId, num) || !(jsonObject["reward"] is JsonArray jsonArray3))
				{
					continue;
				}
				JsonArray jsonArray4 = new JsonArray();
				foreach (JsonNode item2 in jsonArray3)
				{
					jsonArray4.Add(item2?.DeepClone());
				}
				jsonArray.Add(new JsonObject
				{
					["nActId"] = actId,
					["nGirlIdx"] = num,
					["tbAward"] = jsonArray4
				});
			}
		}
		return new JsonObject
		{
			["nActId"] = actId,
			["tbAwardList"] = jsonArray
		};
	}

	private static TrialInfo? FindTrial(int actId, int girlIdx)
	{
		if (!(LoadConfig()?[actId.ToString()] is JsonArray jsonArray))
		{
			return null;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonObject jsonObject) || (jsonObject["idx"]?.GetValue<int>() ?? 0) != girlIdx)
			{
				continue;
			}
			List<int[]> list = new List<int[]>();
			if (jsonObject["reward"] is JsonArray jsonArray2)
			{
				foreach (JsonNode item2 in jsonArray2)
				{
					if (item2 is JsonArray source)
					{
						int[] array = source.Select((JsonNode x) => x?.GetValue<int>() ?? 0).ToArray();
						if (array.Length >= 5)
						{
							list.Add(array);
						}
					}
				}
			}
			return new TrialInfo(list);
		}
		return null;
	}

	private static JsonObject? LoadConfig()
	{
		if (_config != null)
		{
			return _config;
		}
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "activity", "gacha_try.json");
			if (File.Exists(path))
			{
				_config = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
			}
		}
		catch
		{
		}
		return _config;
	}

	private static uint GetSid(int actId)
	{
		return (uint)(actId * 10 + 1);
	}

	private static bool IsClaimed(PlayerInstance player, int actId, int girlIdx)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 50 && x.Sid == GetSid(actId));
		uint num = (uint)(1 << girlIdx - 1);
		return playerAttr != null && (playerAttr.Val & num) != 0;
	}

	private static void MarkClaimed(PlayerInstance player, int actId, int girlIdx, NtfSyncPlayer sync)
	{
		uint sid = GetSid(actId);
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 50 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 50u,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		uint num = (uint)(1 << girlIdx - 1);
		playerAttr.Val |= num;
		sync.Custom[player.ToPackedAttrKey(50u, sid)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(50u, sid)] = playerAttr.Val;
	}
}
