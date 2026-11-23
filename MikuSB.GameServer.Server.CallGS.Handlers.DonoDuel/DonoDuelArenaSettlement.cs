using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

internal static class DonoDuelArenaSettlement
{
	private const uint StarGid = 21u;

	private const uint PassGid = 22u;

	/// <summary>第 490 节：多诺卡属性组（DonoDuel.StateGroupId），巅峰记录都在这一组。</summary>
	private const uint DonoDuelGid = 185u;

	/// <summary>
	/// 第 490 节：「最高击倒敌方目标数量」。
	/// 客户端 <c>DonoDuel.GetArenaKillNum()</c> 的实现是
	/// <c>GetDonoDuelAttribute(self, DonoDuel.ArenaKillNum)</c>，
	/// 而 <c>DonoDuel.ArenaKillNum</c> 本身就是 sid 常量 <c>6</c>（不是数值），
	/// 所以要显示成「6」靠的是 <c>185/6</c> 这个属性，而不是把它写成 6。
	/// 服务端以前从没写过它，界面就一直停在 0。
	/// </summary>
	private const uint ArenaKillRecordSid = 6u;

	private static readonly Dictionary<uint, List<List<uint>>> _rewardCache = new Dictionary<uint, List<List<uint>>>();

	public static async Task<(JsonObject Response, NtfSyncPlayer Sync)> SettleAsync(PlayerInstance player, JsonNode? tbParam)
	{
		JsonObject request = (tbParam as JsonObject) ?? new JsonObject();
		uint levelId = ReadUint(request["nID"]);
		int star = (int)ReadUint(request["nStar"]);
		if (levelId == 0)
		{
			return (Response: new JsonObject { ["sErr"] = "error.BadParam" }, Sync: new NtfSyncPlayer());
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		bool wasPassed = player.IsLevelReallyPassed(levelId);
		List<List<uint>> rewards = ArenaRewards(levelId);
		JsonArray group = new JsonArray();
		if (rewards.Count > 0)
		{
			foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rewards, sync))
			{
				group.Add(item?.DeepClone());
			}
		}
		if (star > 0)
		{
			PlayerAttr starAttr = player.Attributes.GetOrCreate(21u, levelId);
			starAttr.Val |= (uint)((star >= 8) ? 255 : ((1 << star) - 1));
			player.Attributes.SyncTo(sync, starAttr);
		}
		PlayerAttr pass = player.Attributes.GetOrCreate(22u, levelId);
		pass.Val = now;
		player.Attributes.SyncTo(sync, pass);
		// 第 456 节：把本次通关推进到剧情任务（objectives=1 的「通关 xx 关」）。
		// 客户端的提交剧情（DonoDuel_phase1_02 这类）只有在任务变成「可提交」后，
		// 由 DonoDuelTask:ReqGetReward 用 CartoonMgr 播出来。
		MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.AdvanceArenaClearMissions(player, levelId, sync);
		// 第 489 节：Objectives=9「出阵【指定卡】，在【指定关卡】中累计击倒 N 个目标」。
		// 客户端 Req_LevelSettlement 会把本局击杀明细塞进 tbKill（DonoDuelMgr:GetKillsData），
		// 以前整包被丢掉，所以 403「崭露头角 0/5」打完永远不动。
		TrackKills(player, levelId, request, star > 0, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonArray awards = new JsonArray();
		if (group.Count > 0)
		{
			awards.Add(group);
		}
		JsonObject response = new JsonObject
		{
			["tbAward"] = awards,
			["tbRewards"] = awards.DeepClone(),
			["nStar"] = star,
			["bFirstPass"] = !wasPassed
		};
		return (Response: response, Sync: sync);
	}

	/// <summary>
	/// 第 489 节：结算 / 失败都走这里，把本局击杀数记到 Objectives=9 的剧情任务上。
	/// 返回 (本局击杀, 推进的任务数)。
	/// </summary>
	public static (int Kills, int Tasks) TrackKills(PlayerInstance player, uint levelId, JsonObject request, bool victory, NtfSyncPlayer sync)
	{
		int kills = ReadKills(request, levelId, victory);
		UpdateKillRecord(player, ReadRealKills(request), sync);
		int tasks = MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.AdvanceArenaKillTasks(player, levelId, kills, sync);
		LogArenaKill(player, levelId, victory ? 1 : 0, kills, tasks, request["tbKill"]?.ToJsonString());
		return (kills, tasks);
	}

	/// <summary>
	/// 第 490 节：把本局击杀数并入「最高击倒敌方目标数量」（185/6）。
	/// 只增不减，失败局也算——界面统计的是单局最高击杀，不看胜负。
	/// </summary>
	private static void UpdateKillRecord(PlayerInstance player, int kills, NtfSyncPlayer sync)
	{
		if (kills <= 0)
		{
			return;
		}
		PlayerAttr record = player.Attributes.GetOrCreate(DonoDuelGid, ArenaKillRecordSid);
		if ((uint)kills <= record.Val)
		{
			return;
		}
		record.Val = (uint)kills;
		player.Attributes.SyncTo(sync, record);
	}

	/// <summary>
	/// 从结算请求里读本局击杀数。客户端 tbKill 来自 DonoDuelMgr:GetKillsData()，
	/// 实测其它 DLC（VirCapture / 沙盒推演）都是「数字数组，求和即击杀」，
	/// 这里同时兼容对象形态和 [怪物ID, 数量] 的成对形态。
	/// </summary>
	private static int ReadKills(JsonObject request, uint levelId, bool victory)
	{
		int sum = ReadRealKills(request);
		if (sum > 0)
		{
			return sum;
		}
		// tbKill 缺失时的兜底：通关就按本关怪物总数记，失败记 0。
		// 注意这个兜底值只用于任务推进，不会写进「最高击杀」记录。
		return victory ? MonsterCount(levelId) : 0;
	}

	/// <summary>只取客户端上报的真实击杀数，不做任何兜底。</summary>
	private static int ReadRealKills(JsonObject request)
	{
		int sum = SumKills(request["tbKill"]);
		if (sum > 0)
		{
			return sum;
		}
		foreach (string key in new string[3] { "nKill", "nKillNum", "nKillCount" })
		{
			int direct = (int)ReadUint(request[key]);
			if (direct > 0)
			{
				return direct;
			}
		}
		return 0;
	}

	private static int SumKills(JsonNode? node)
	{
		if (node is JsonValue)
		{
			return (int)ReadUint(node);
		}
		int total = 0;
		if (node is JsonArray array)
		{
			foreach (JsonNode child in array)
			{
				if (child is JsonArray pair)
				{
					// 实测 tbKill 形如 [[0,0],[202,2],[184,1],[349,1],[8,1]]：
					// [怪物ID, 数量]，首项是全 0 占位，数量为 0 的项不计入。
					int count = (int)ReadUint(pair.Count > 0 ? pair[pair.Count - 1] : null);
					if (count > 0)
					{
						total += count;
					}
				}
				else
				{
					total += (int)ReadUint(child);
				}
			}
			return total;
		}
		if (node is JsonObject obj)
		{
			foreach (KeyValuePair<string, JsonNode> item in obj)
			{
				total += SumKills(item.Value);
			}
		}
		return total;
	}

	/// <summary>donoduel_arena_level.json 里该关配置的怪物总数（10 波 × 5 只 = 50）。</summary>
	private static int MonsterCount(uint levelId)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "DonoDuel", "donoduel_arena_level.json");
			if (!File.Exists(path))
			{
				return 0;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path)).AsArray())
			{
				if (item is not JsonObject row || ReadUint(row["ID"]) != levelId)
				{
					continue;
				}
				if (row["Monster"] is not JsonArray monsters)
				{
					return 0;
				}
				int count = 0;
				foreach (JsonNode wave in monsters)
				{
					count += ((wave is JsonArray inner) ? inner.Count : 1);
				}
				return count;
			}
		}
		catch
		{
		}
		return 0;
	}

	private static void LogArenaKill(PlayerInstance player, uint levelId, int star, int kills, int tasks, string? rawKill)
	{
		try
		{
			string text = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] uid={1} level={2} star={3} kills={4} tasks={5} tbKill={6}\n",
				DateTime.Now, player.Uid, levelId, star, kills, tasks, rawKill ?? "null");
			File.AppendAllText(Path.Combine("D:\\DSH\\.tools", "dono_arena_kill.log"), text);
		}
		catch
		{
		}
	}

	private static List<List<uint>> ArenaRewards(uint levelId)
	{
		if (_rewardCache.TryGetValue(levelId, out List<List<uint>> value))
		{
			return value;
		}
		List<List<uint>> list = new List<List<uint>>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "DonoDuel";
			buffer[4] = "donoduel_arena_battle.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path)).AsArray())
				{
					if (!(item is JsonObject jsonObject) || ReadUint(jsonObject["LevelID"]) != levelId)
					{
						continue;
					}
					if (!(jsonObject["Reward"] is JsonArray jsonArray))
					{
						break;
					}
					foreach (JsonNode item2 in jsonArray)
					{
						if (!(item2 is JsonArray { Count: >=5 } jsonArray2))
						{
							continue;
						}
						List<uint> list2 = new List<uint>(5);
						foreach (JsonNode item3 in jsonArray2.Take(5))
						{
							list2.Add(ReadUint(item3));
						}
						if (list2.Count == 5 && list2[4] != 0)
						{
							list.Add(list2);
						}
					}
					break;
				}
			}
		}
		catch
		{
		}
		_rewardCache[levelId] = list;
		return list;
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		if (node is JsonValue jsonValue2 && jsonValue2.TryGetValue<int>(out var value2))
		{
			return (value2 >= 0) ? ((uint)value2) : 0u;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
