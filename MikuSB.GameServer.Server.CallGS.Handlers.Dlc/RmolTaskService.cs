using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 450 节：第 32 期「绘梦沙盘 / 沙盒推演」→ 繁荣嘉奖 → 绘梦档案（周常任务）的进度与领奖。
///
/// 客户端侧链路（反汇编 rmolreward 控件 + _G.Achievement 得到）：
///   任务列表  umg_rmol_reward 的 tbTaskList（来自 achievement.GetQuestConfig）
///   IsFinish  Achievement.IsFinished(cfg)  → GetProgres(nGroupId=7, nId) >= nCheckValue
///   已领      Achievement.IsReceive(cfg)   → 属性 (7, nId) 的 bit0
///   进度      Achievement.GetProgres       → GetBits(me:GetAttribute(7, nId), 1, 31)
///   领奖      me:CallGS("DLC_RMOL_Get_Task_Awards", …)
///             → EventSystem.Trigger("DLC_RMOL_Get_Task_Awards_Return", tb.Tasks, tb.Awards)
///
/// 任务定义只在 Resources/achievement/activity_quest.json（客户端 LoadQuestConf 读的就是这张表）：
///   20601 Type 364「任意一局绘梦沙盘获胜」          CheckValue 1
///   20602 Type 358「完成 沙盒推演-2」Param [10002]   CheckValue 1
///   20603 Type 358  Param [10004] / 20604 [10006] / 20605 [10008]
///   20606 Type 359「棋盘挑战获得 2 颗星」           CheckValue 2
///   20607 Type 359「棋盘挑战获得 6 颗星」           CheckValue 6
///
/// 注意 achievement.json 里也有同 ID（20601~20607，Type 3 抽卡任务组 3）的条目，
/// 但绘梦档案界面 / GetQuestConfig(20601).nGroupId 都是 7，所以这里只认 activity_quest.json。
///
/// 星级位：关卡属性 (173, 关卡ID) 的 bit1/bit2/bit3 = 三颗星（Level:StarRecord 读），
/// bit31 = 已通关（Level:IsPassed 读）。
/// </summary>
internal static class RmolTaskService
{
	/// <summary>活动任务属性组（Achievement.GroupID_Quest = 7）。</summary>
	public const uint QuestGid = 7u;

	/// <summary>DLC_RMOL.LevelGID：关卡通关 / 星级位。</summary>
	public const uint LevelGid = 173u;

	/// <summary>bit31 被 Level:IsPassed 当作「已通关」。</summary>
	public const uint PassedBit = 0x80000000u;

	/// <summary>bit0 = 已领奖，bit1 起是进度。</summary>
	private const uint ClaimedBit = 1u;

	private const int ProgressShift = 1;

	/// <summary>绘梦档案任务的三种 Type（364 胜场 / 358 指定关卡 / 359 棋盘星数）。</summary>
	private static readonly HashSet<int> RmolTaskTypes = new HashSet<int> { 358, 359, 364 };

	private static readonly object CacheLock = new object();

	private static List<JsonObject> _rows = new List<JsonObject>();

	private static DateTime cacheStamp = DateTime.MinValue;

	/// <summary>只保留绘梦沙盘的任务行（避免把别的活动任务一起处理）。</summary>
	public static List<JsonObject> Rows()
	{
		lock (CacheLock)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
			if (stamp == cacheStamp)
			{
				return _rows;
			}
			_rows = new List<JsonObject>();
			cacheStamp = stamp;
			try
			{
				if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray array)
				{
					foreach (JsonNode? node in array)
					{
						if (node is JsonObject row && RmolTaskTypes.Contains(Int(row, "Type")))
						{
							_rows.Add(row);
						}
					}
				}
			}
			catch
			{
			}
			return _rows;
		}
	}

	public static JsonObject? Find(int taskId)
	{
		return Rows().FirstOrDefault((JsonObject x) => Int(x, "ID") == taskId);
	}

	public static int Int(JsonObject row, string key)
	{
		JsonNode? node;
		return row.TryGetPropertyValue(key, out node) && int.TryParse(node?.ToString(), out int value) ? value : 0;
	}

	public static int Need(JsonObject row)
	{
		int need = Int(row, "CheckValue");
		return need <= 0 ? 1 : need;
	}

	public static uint GetProgress(PlayerInstance player, int taskId)
	{
		return (player.Attributes.GetOrCreate(QuestGid, (uint)taskId).Val >> ProgressShift) & 0x7FFFFFFFu;
	}

	/// <summary>关卡三颗星（bit1~bit3）的总数，供 Type 359 用。</summary>
	public static int TotalStars(PlayerInstance player)
	{
		int total = 0;
		foreach (PlayerAttr attr in player.Data.Attrs)
		{
			if (attr.Gid != LevelGid)
			{
				continue;
			}
			for (int bit = 1; bit <= 3; bit++)
			{
				if ((attr.Val & (1u << bit)) != 0)
				{
					total++;
				}
			}
		}
		return total;
	}

	/// <summary>进度只增不减，且不超过 CheckValue。</summary>
	public static void SetProgress(PlayerInstance player, int taskId, int value, NtfSyncPlayer? sync)
	{
		JsonObject? row = Find(taskId);
		if (row == null || value <= 0)
		{
			return;
		}
		uint need = (uint)Need(row);
		PlayerAttr attr = player.Attributes.GetOrCreate(QuestGid, (uint)taskId);
		uint cur = (attr.Val >> ProgressShift) & 0x7FFFFFFFu;
		uint next = Math.Min(Math.Max(cur, (uint)value), need);
		if (next == cur)
		{
			return;
		}
		attr.Val = (attr.Val & ClaimedBit) | (next << ProgressShift);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
	}

	/// <summary>推进某个 Type 下所有任务（358 需要按 Param 的关卡号过滤，传 levelId）。</summary>
	public static void Advance(PlayerInstance player, int type, int? levelId, NtfSyncPlayer? sync)
	{
		foreach (JsonObject row in Rows())
		{
			if (Int(row, "Type") != type)
			{
				continue;
			}
			if (levelId.HasValue && !ParamContains(row, levelId.Value))
			{
				continue;
			}
			SetProgress(player, Int(row, "ID"), Need(row), sync);
		}
	}

	/// <summary>通关结算：胜场 / 指定关卡 / 棋盘星数三件事一起推进。</summary>
	public static void OnLevelEnd(PlayerInstance player, int levelId, bool victory, NtfSyncPlayer sync)
	{
		if (victory)
		{
			Advance(player, 364, null, sync);
			Advance(player, 358, levelId, sync);
		}
		// 359 是「累计星数」，直接按当前星级重算；失败局也刷新一次，避免历史星数漏算。
		int stars = TotalStars(player);
		foreach (JsonObject row in Rows())
		{
			if (Int(row, "Type") == 359)
			{
				SetProgress(player, Int(row, "ID"), stars, sync);
			}
		}
	}

	/// <summary>登录/重连时按已有的通关、星级补齐进度（老存档也能拿到该领的奖励）。</summary>
	public static void Ensure(PlayerInstance player)
	{
		try
		{
			bool hasWin = player.Data.Attrs.Any((PlayerAttr x) => x.Gid == LevelGid && (x.Val & PassedBit) != 0);
			int stars = TotalStars(player);
			bool changed = false;
			foreach (JsonObject row in Rows())
			{
				int type = Int(row, "Type");
				int id = Int(row, "ID");
				bool done = type switch
				{
					364 => hasWin,
					358 => ParamContainsPassed(player, row),
					359 => stars >= Need(row),
					_ => false
				};
				if (!done)
				{
					continue;
				}
				PlayerAttr attr = player.Attributes.GetOrCreate(QuestGid, (uint)id);
				uint need = (uint)Need(row);
				uint cur = (attr.Val >> ProgressShift) & 0x7FFFFFFFu;
				if (cur >= need)
				{
					continue;
				}
				attr.Val = (attr.Val & ClaimedBit) | (need << ProgressShift);
				changed = true;
			}
			if (changed)
			{
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 第 451/452 节：每领一个绘梦档案任务额外给的繁荣经验，用于升「缤纷补给」等级。
	///
	/// 任务表 activity_quest.json 只配了代币奖励（[[4,1,30,1,800]]），没有经验字段；
	/// 繁荣嘉奖的等级条（DLC_RMOL.AttrGID=172 / sid=100）由「任务 + 对局」共同推进：
	///   打完一关 +300（DLC_RMOL_LevelEnd 的 WinExp），
	///   领一个任务 +800（本常量，一个任务正好顶一次通关）。
	/// </summary>
	public const int TaskExp = 800;

	/// <summary>DLC_RMOL.AttrGID：成长经验 / 成长奖励领取位图。</summary>
	public const uint GrowAttrGid = 172u;

	/// <summary>DLC_RMOL.SessionRange=100、GrowExpOffset=0：经验 sid = 期数 * 100，第 1 期即 100。</summary>
	public const int GrowExpSid = 100;

	/// <summary>给繁荣嘉奖加经验并同步给客户端，返回累计经验。</summary>
	public static uint AddGrowExp(PlayerInstance player, int amount, NtfSyncPlayer sync)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(GrowAttrGid, (uint)GrowExpSid);
		attr.Val = (uint)Math.Min(uint.MaxValue, Math.Max(0L, (long)attr.Val + amount));
		player.Attributes.SyncTo(sync, attr);
		return attr.Val;
	}

	/// <summary>领奖：返回实际领到的任务 ID、发出去的五元组奖励、以及这次加的活动经验。</summary>
	public static async Task<(List<int> Claimed, JsonArray Awards, int ExpGain)> ClaimAsync(
		PlayerInstance player, IEnumerable<int> taskIds, NtfSyncPlayer sync)
	{
		List<int> claimed = new List<int>();
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>>();
		foreach (int taskId in taskIds.Distinct())
		{
			JsonObject? row = Find(taskId);
			if (row == null)
			{
				continue;
			}
			PlayerAttr attr = player.Attributes.GetOrCreate(QuestGid, (uint)taskId);
			if ((attr.Val & ClaimedBit) != 0)
			{
				continue;
			}
			if ((attr.Val >> ProgressShift) < (uint)Need(row))
			{
				continue;
			}
			List<IReadOnlyList<uint>> rewards = ReadRewards(row);
			if (rewards.Count == 0)
			{
				continue;
			}
			rows.AddRange(rewards);
			attr.Val |= ClaimedBit;
			player.Attributes.SyncTo(sync, attr);
			claimed.Add(taskId);
		}
		JsonArray awards = new JsonArray();
		int expGain = claimed.Count * TaskExp;
		if (rows.Count > 0)
		{
			awards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			if (player.InventoryManager != null)
			{
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}
		}
		if (expGain > 0)
		{
			AddGrowExp(player, expGain, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return (claimed, awards, expGain);
	}

	/// <summary>当前所有能领的任务 ID（用于「一键领取」式的空参数请求）。</summary>
	public static List<int> Claimable(PlayerInstance player)
	{
		List<int> list = new List<int>();
		foreach (JsonObject row in Rows())
		{
			int id = Int(row, "ID");
			PlayerAttr? attr = player.Attributes.Get(QuestGid, (uint)id);
			if (attr == null)
			{
				continue;
			}
			if ((attr.Val & ClaimedBit) == 0 && (attr.Val >> ProgressShift) >= (uint)Need(row))
			{
				list.Add(id);
			}
		}
		return list;
	}

	private static List<IReadOnlyList<uint>> ReadRewards(JsonObject row)
	{
		List<IReadOnlyList<uint>> list = new List<IReadOnlyList<uint>>();
		if (row["Rewards"] is not JsonArray rewards)
		{
			return list;
		}
		foreach (JsonArray node in rewards.OfType<JsonArray>())
		{
			List<uint> values = node.Select(ReadUInt).Take(5).ToList();
			if (values.Count >= 5 && values[0] != 0 && values[4] != 0)
			{
				list.Add(values);
			}
		}
		return list;
	}

	private static bool ParamContains(JsonObject row, int levelId)
	{
		return row["Param"] is JsonArray param && param.Any((JsonNode x) => int.TryParse(x?.ToString(), out int v) && v == levelId);
	}

	private static bool ParamContainsPassed(PlayerInstance player, JsonObject row)
	{
		if (row["Param"] is not JsonArray param)
		{
			return false;
		}
		foreach (JsonNode node in param)
		{
			if (int.TryParse(node?.ToString(), out int levelId) && levelId > 0 && (player.GetAttr(LevelGid, (uint)levelId) & PassedBit) != 0)
			{
				return true;
			}
		}
		return false;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		return uint.TryParse(node?.ToString(), out uint value) ? value : 0u;
	}
}
