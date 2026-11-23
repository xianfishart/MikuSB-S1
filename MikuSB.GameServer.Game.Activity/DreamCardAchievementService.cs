using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 第 494 节：纷愿绮梦（DreamCard）活动成就 20201~20241 的进度推进。
///
/// 现象：奖励页「心流奇境」页签里所有条目恒为 0/N，右侧领取按钮永远灰。
/// 客户端读进度的路径：Achievement.GetProgres(7, id) → me:GetAttribute(7, id)，
/// 也就是属性组 7 的低位 bit1 起是进度、bit0 是「已领取」。
/// 服务端此前只有少数活动（星祈心愿/农场/拼图/回归…）会写组 7，
/// DreamCard 这条线没有任何地方推进，导致整页 0。
///
/// 客户端结算 DreamCard_LevelSettlement 时其实带了完整局内统计
/// （反汇编 DreamCard.LevelSettlement）：CardStarCount / TreStarCount /
/// DestroyCount / nStage / nLevel / BuildEnergy / nScore / tbUpScore。
/// 这里按 activity_quest.json 里的 Type 分派把它们累加进组 7：
///   285 无 Param        → 累计完成任意关卡次数
///   285 Param[0]=2      → 累计完成任意纷彩世界关卡次数
///   285 Param[0]=3      → 参与永恒绮梦关卡次数
///   282                 → 累计击败敌人数（用 nStage 近似：每层至少一场战斗）
///   280                 → 累计获得棋子数（CardStarCount 求和）
///   281                 → 累计获得礼物数（TreStarCount 求和）
///   283                 → 累计提升漫溯等级次数（用最终 nLevel 近似）
///   292 Param[0]=4      → 漫溯等级提升到 4 的次数（本局 nLevel ≥ 4 计一次）
///   278                 → 单局获得棋子数（取最大值）
///   279                 → 单局消耗棋子数（DestroyCount，取最大值）
///   284 Param[0]=流派    → 该流派累计漫溯能量（BuildEnergy[流派]）
///   286 Param[0]=3      → 永恒绮梦最高积分（nScore，取最大值）
///
/// 另外登录时按关卡记录（属性组 152，sid=10*关卡+1 是通关次数）补记一次
/// 「累计完成任意关卡」，让老存档不至于整页 0。
/// </summary>
public static class DreamCardAchievementService
{
	public const uint QuestGid = 7u;

	public const uint LevelGid = 152u;

	/// <summary>挑战期号落在属性 152/0（客户端 luckyreward.OnOpen 用它查 tbChallTimeConf）。</summary>
	public const uint ChallPeriodSid = 0u;

	private const uint ClaimedBit = 1u;

	private const int ProgressShift = 1;

	private const uint AtlasCompleteType = 285u;

	private const uint EnemyKillType = 282u;

	private const uint CardGainType = 280u;

	private const uint TreasureGainType = 281u;

	private const uint LevelUpType = 283u;

	private const uint LevelUpFourType = 292u;

	private const uint CardMaxOneType = 278u;

	private const uint DestroyMaxOneType = 279u;

	private const uint BuildEnergyType = 284u;

	private const uint EndlessScoreType = 286u;

	/// <summary>按「无 Param」的活动任务累加/抬高进度，返回改动的任务条数。</summary>
	public static int AdvanceNoParam(PlayerInstance player, uint type, long amount, bool useMax, NtfSyncPlayer? sync)
	{
		return Advance(player, type, null, amount, useMax, sync);
	}

	/// <summary>按 Param[0] 匹配的活动任务累加/抬高进度，返回改动的任务条数。</summary>
	public static int AdvanceWithParam(PlayerInstance player, uint type, uint paramKey, long amount, bool useMax, NtfSyncPlayer? sync)
	{
		return Advance(player, type, paramKey, amount, useMax, sync);
	}

	private static int Advance(PlayerInstance player, uint type, uint? paramKey, long amount, bool useMax, NtfSyncPlayer? sync)
	{
		if (player == null || amount <= 0)
		{
			return 0;
		}
		int changed = 0;
		foreach (KeyValuePair<uint, ActivityQuestExcel> pair in GameData.ActivityQuestData)
		{
			ActivityQuestExcel quest = pair.Value;
			if (quest == null || quest.Type != type)
			{
				continue;
			}
			List<uint> ps = ReadParams(quest.Param);
			if (paramKey.HasValue)
			{
				if (ps.Count == 0 || ps[0] != paramKey.Value)
				{
					continue;
				}
			}
			else if (ps.Count > 0)
			{
				continue;
			}
			uint need = quest.CheckValue == 0 ? 1u : quest.CheckValue;
			PlayerAttr attr = player.Attributes.GetOrCreate(QuestGid, pair.Key);
			uint cur = (attr.Val >> ProgressShift) & 0x7FFFFFFFu;
			if (cur >= need)
			{
				continue;
			}
			long raw = useMax ? Math.Max(cur, amount) : (long)cur + amount;
			if (raw < 0)
			{
				raw = 0;
			}
			uint next = (uint)Math.Min(raw, need);
			if (next <= cur)
			{
				continue;
			}
			attr.Val = (attr.Val & ClaimedBit) | (next << ProgressShift);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
			changed++;
		}
		if (changed > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>把一局结算的统计按类型推进（详见类注释的映射表）。</summary>
	public static int AdvanceBySettlement(PlayerInstance player, int levelType, int stage, int level, int score,
		JsonNode? cardStarCount, JsonNode? treasureStarCount, JsonNode? destroyCount, JsonNode? buildEnergy, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		// 任何关卡都算「完成一次任意关卡」。
		changed += AdvanceNoParam(player, AtlasCompleteType, 1, false, sync);
		if (levelType == 1 || levelType == 2)
		{
			// 纷彩世界关卡：普通关（nType=1，levellist.json 那批）和挑战关（nType=2）都算。
			// 实测进入普通关的包是 DreamCard_EnterLevel {"nID":3,"nType":1}，
			// 只认 nType=2 时这条成就永远不动。
			changed += AdvanceWithParam(player, AtlasCompleteType, 2u, 1, false, sync);
		}
		else if (levelType == 3)
		{
			// 永恒绮梦（无尽）
			changed += AdvanceWithParam(player, AtlasCompleteType, 3u, 1, false, sync);
		}
		long cards = SumNumbers(cardStarCount);
		long treasures = SumNumbers(treasureStarCount);
		long destroyed = SumNumbers(destroyCount);
		if (cards > 0)
		{
			changed += AdvanceNoParam(player, CardGainType, cards, false, sync);
			changed += AdvanceNoParam(player, CardMaxOneType, cards, true, sync);
		}
		if (treasures > 0)
		{
			changed += AdvanceNoParam(player, TreasureGainType, treasures, false, sync);
		}
		if (destroyed > 0)
		{
			changed += AdvanceNoParam(player, DestroyMaxOneType, destroyed, true, sync);
		}
		if (stage > 0)
		{
			changed += AdvanceNoParam(player, EnemyKillType, stage, false, sync);
		}
		if (level > 0)
		{
			changed += AdvanceNoParam(player, LevelUpType, level, false, sync);
		}
		if (level >= 4)
		{
			changed += AdvanceWithParam(player, LevelUpFourType, 4u, 1, false, sync);
		}
		if (levelType == 3)
		{
			if (score > 0)
			{
				changed += AdvanceWithParam(player, EndlessScoreType, 3u, score, true, sync);
			}
			// 无尽模式的流派能量按「流派 ID → 本次提升分」给出 tbUpScore，
			// 同时结算包里也带 BuildEnergy（流派 → 累计能量）。
			changed += AdvanceBuildEnergy(player, buildEnergy, sync);
		}
		else
		{
			changed += AdvanceBuildEnergy(player, buildEnergy, sync);
		}
		return changed;
	}

	private static int AdvanceBuildEnergy(PlayerInstance player, JsonNode? buildEnergy, NtfSyncPlayer? sync)
	{
		int changed = 0;
		// 实测结算包里是**数组**：BuildEnergy = [7016,null,null,6532]，
		// 下标就是流派 ID（1 起），也就是 activity_quest 里 284 那批 Param 的取值；
		// 空位是 null。之前只认 JsonObject，导致 20225~20241 永恒为 0。
		if (buildEnergy is JsonArray arr)
		{
			for (int i = 0; i < arr.Count; i++)
			{
				long value = SumNumbers(arr[i]);
				if (value > 0)
				{
					changed += AdvanceWithParam(player, BuildEnergyType, (uint)(i + 1), value, false, sync);
				}
			}
			return changed;
		}
		if (buildEnergy is not JsonObject obj)
		{
			return 0;
		}
		foreach (KeyValuePair<string, JsonNode?> pair in obj)
		{
			if (!uint.TryParse(pair.Key, out uint buildId))
			{
				continue;
			}
			long value = SumNumbers(pair.Value);
			if (value > 0)
			{
				changed += AdvanceWithParam(player, BuildEnergyType, buildId, value, false, sync);
			}
		}
		return changed;
	}

	/// <summary>登录补记：按属性 152 的通关次数把「累计完成任意关卡」抬到下限。</summary>
	public static int BackfillFromLevelRecords(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player?.Data?.Attrs == null)
		{
			return 0;
		}
		long total = 0;
		foreach (PlayerAttr attr in player.Data.Attrs)
		{
			if (attr.Gid != LevelGid || attr.Sid < 11 || attr.Sid % 10 != 1)
			{
				continue;
			}
			total += attr.Val;
		}
		if (total <= 0)
		{
			return 0;
		}
		return AdvanceNoParam(player, AtlasCompleteType, total, true, sync);
	}

	/// <summary>把挑战期号写进 152/0（客户端奖励页据此判断「纷彩世界」是否开放）。</summary>
	public static int EnsureChallPeriod(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint latest = ResolveLatestChallPeriodId();
		if (player == null || latest == 0)
		{
			return 0;
		}
		PlayerAttr attr = player.Attributes.GetOrCreate(LevelGid, ChallPeriodSid);
		if (attr.Val == latest)
		{
			return 0;
		}
		attr.Val = latest;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return 1;
	}

	private static uint _latestPeriodId;

	private static uint ResolveLatestChallPeriodId()
	{
		if (_latestPeriodId != 0)
		{
			return _latestPeriodId;
		}
		try
		{
			string path = Path.Combine(ConfigManager.Config.Path.ResourcePath, "dlc", "DreamCard", "chall_time.json");
			if (!File.Exists(path))
			{
				return 0;
			}
			JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
			if (node is not JsonArray arr)
			{
				return 0;
			}
			uint best = 0;
			foreach (JsonNode? row in arr)
			{
				if (row is not JsonObject obj)
				{
					continue;
				}
				uint id = ReadUInt(obj["ChallTimeID"]);
				if (id > best)
				{
					best = id;
				}
			}
			_latestPeriodId = best;
			return best;
		}
		catch
		{
			return 0;
		}
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue v)
		{
			if (v.TryGetValue<uint>(out uint u))
			{
				return u;
			}
			if (v.TryGetValue<int>(out int i) && i > 0)
			{
				return (uint)i;
			}
			if (v.TryGetValue<string>(out string? s) && uint.TryParse(s, out uint p))
			{
				return p;
			}
		}
		return 0u;
	}

	/// <summary>把任意 JSON 结构里的数字全部相加（数组/对象递归）。</summary>
	public static long SumNumbers(JsonNode? node)
	{
		switch (node)
		{
		case null:
			return 0;
		case JsonArray arr:
		{
			long sum = 0;
			foreach (JsonNode? item in arr)
			{
				sum += SumNumbers(item);
			}
			return sum;
		}
		case JsonObject obj:
		{
			long sum2 = 0;
			foreach (KeyValuePair<string, JsonNode?> pair in obj)
			{
				sum2 += SumNumbers(pair.Value);
			}
			return sum2;
		}
		case JsonValue v:
			if (v.TryGetValue<long>(out long l))
			{
				return l;
			}
			if (v.TryGetValue<int>(out int i2))
			{
				return i2;
			}
			if (v.TryGetValue<double>(out double d))
			{
				return (long)d;
			}
			if (v.TryGetValue<string>(out string? s) && long.TryParse(s, out long p))
			{
				return p;
			}
			return 0;
		default:
			return 0;
		}
	}

	private static List<uint> ReadParams(JToken? token)
	{
		List<uint> list = new List<uint>();
		if (token is JArray jArray)
		{
			foreach (JToken item in jArray)
			{
				if (item.Type == JTokenType.Integer)
				{
					list.Add((uint)(long)item);
				}
				else if (item.Type == JTokenType.String && uint.TryParse(item.ToString(), out uint v))
				{
					list.Add(v);
				}
			}
		}
		else if (token != null && token.Type == JTokenType.Integer)
		{
			list.Add((uint)(long)token);
		}
		return list;
	}
}
