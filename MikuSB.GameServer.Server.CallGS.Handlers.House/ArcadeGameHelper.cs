using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 推币大赢家（ArcadeGame / CoinPusher / 基地街机）的等级与等级奖励口径。
/// 2026-11-13 新增（修复记录第 267 节）。
///
/// 运行时探针（客户端 HouseArcadeToSeverMsg 反汇编）确认的属性：
///   101/18002 = 累计经验   ← HouseStorage.GetArcadeAttr(Attr_Exp)，客户端 GetExp 直接读它
///   101/18003 = 等级奖励位图 ← Attr_LevelRewardGot，GetBits(val, level, level)，即 bit = 等级
///   101/18004 = 无尽模式最高分
///   101/18005 = 已通关少女位图
/// **没有单独存等级的属性**：客户端 GetLevel() 用累计经验去比 coinpusher_rewards.json 的
/// Exp 阈值（逐级扣减，扣不动为止），所以我们写满经验就等于满级。
///
/// 旧实现在 ArcadeGameGetLevelReward 里拼的是 Resources/house/coinpusher_rewards.json，
/// 实际文件在 Resources/house/coinpusher/coinpusher_rewards.json，永远读到 null，
/// 于是「领取」只回空包，界面一直停在可领取 —— 这就是本次要修的病灶。
/// </summary>
internal static class ArcadeGameHelper
{
	/// <summary>累计经验（客户端 GetExp 读的就是它）。</summary>
	internal const uint ExpSid = 18002u;

	/// <summary>等级奖励位图，bit = 等级。</summary>
	internal const uint LevelRewardGotSid = 18003u;

	/// <summary>等级奖励位图能表达的位数上限（uint）。</summary>
	internal const int MaxRewardBit = 31;

	private static List<Row>? _rows;

	internal struct Row
	{
		public int Level;

		public int Exp;

		public List<uint> Reward;
	}

	/// <summary>读 Resources/house/coinpusher/coinpusher_rewards.json（客户端 HouseArcadeConfig.tbRewardsConfig 同源）。</summary>
	internal static List<Row> LoadRows()
	{
		if (_rows != null)
		{
			return _rows;
		}
		List<Row> list = new List<Row>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "coinpusher", "coinpusher_rewards.json");
			if (File.Exists(path))
			{
				JsonArray? jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode node in jsonArray)
					{
						if (!(node is JsonObject jsonObject))
						{
							continue;
						}
						int level = HouseJson.NumField(jsonObject, "Level");
						if (level <= 0)
						{
							continue;
						}
						List<uint> reward = new List<uint>();
						if (jsonObject["Reward"] is JsonArray source)
						{
							foreach (JsonNode item in source)
							{
								reward.Add((uint)Math.Max(0, HouseJson.ToInt(item)));
							}
						}
						list.Add(new Row
						{
							Level = level,
							Exp = Math.Max(0, HouseJson.NumField(jsonObject, "Exp")),
							Reward = reward
						});
					}
				}
			}
		}
		catch
		{
		}
		list.Sort((Row a, Row b) => a.Level.CompareTo(b.Level));
		_rows = list;
		return list;
	}

	internal static int MaxLevel()
	{
		List<Row> rows = LoadRows();
		return (rows.Count == 0) ? 0 : rows[rows.Count - 1].Level;
	}

	/// <summary>满级需要的累计经验 = 1~max-1 级阈值之和（当前 12000）。</summary>
	internal static int MaxExp()
	{
		int max = MaxLevel();
		int sum = 0;
		foreach (Row row in LoadRows())
		{
			if (row.Level < max && row.Exp > 0)
			{
				sum += row.Exp;
			}
		}
		return sum;
	}

	internal static int ReadExp(PlayerInstance player)
	{
		return Math.Max(0, (int)HouseAttr.Read(player, ExpSid));
	}

	/// <summary>与客户端 GetLevel 同口径：从 1 级起，逐级扣经验，扣不动就停。</summary>
	internal static int LevelFromExp(int exp)
	{
		List<Row> rows = LoadRows();
		if (rows.Count == 0)
		{
			return 1;
		}
		int max = rows[rows.Count - 1].Level;
		int level = 1;
		long left = Math.Max(0, exp);
		foreach (Row row in rows)
		{
			if (row.Level >= max || row.Exp <= 0)
			{
				continue;
			}
			if (left < row.Exp)
			{
				break;
			}
			left -= row.Exp;
			level = row.Level + 1;
		}
		return Math.Clamp(level, 1, Math.Max(1, max));
	}

	internal static List<uint>? LoadReward(int level)
	{
		foreach (Row row in LoadRows())
		{
			if (row.Level == level)
			{
				return row.Reward;
			}
		}
		return null;
	}

	// 2026-11-13 追加（修复记录第 270 节）：无尽模式解锁口径。
	// 客户端 UMG/House/HouseArcade/umg_arcade_selected.lua 的 GetEndlessUnlocked() 只做一件事：
	//   遍历 HouseArcadeConfig.tbGirlConfig，只要有一位少女
	//   HouseArcadeToSeverMsg:GetEndlessModeState(nGirlID) 为真，就认为无尽模式已解锁。
	// 而 GetEndlessModeState = GetBits(Attr_GirlEndlessModeState, gid, gid)，
	// Attr_GirlEndlessModeState = 5 → 101/18005 的第 gid 位。
	// 官方文案 Coinpusher_Tip_ChallengeGirlLock 是「和她完成普通模式5回合后，才能在无尽模式里相遇」，
	// 也就是 18005 该位表示「该少女的无尽模式已解锁」，由普通模式累计回合达标触发，
	// 而不是「在无尽模式里遇到过」（旧实现只写了后者，导致无尽模式永远进不去 → 死锁）。

	/// <summary>少女无尽模式解锁位图（bit = 少女ID）。</summary>
	internal const uint GirlEndlessStateSid = 18005u;

	/// <summary>少女普通模式累计回合起点 Sid（Sid = 18010 + 少女ID）。</summary>
	internal const uint GirlNormalStateSid = 18010u;

	private static Dictionary<int, int>? _challengeUnlock;

	/// <summary>coinpusher_mode.json 里该少女普通模式关卡要求的回合数（ChallengeUnlock，缺省 5）。</summary>
	internal static int ChallengeUnlockFor(int girlId)
	{
		if (_challengeUnlock == null)
		{
			Dictionary<int, int> map = new Dictionary<int, int>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "coinpusher", "coinpusher_mode.json");
				if (File.Exists(path))
				{
					JsonArray? jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode node in jsonArray)
						{
							if (!(node is JsonObject jsonObject))
							{
								continue;
							}
							int gid = HouseJson.NumField(jsonObject, "GirlID");
							int unlock = HouseJson.NumField(jsonObject, "ChallengeUnlock");
							if (gid > 0 && unlock > 0)
							{
								map[gid] = unlock;
							}
						}
					}
				}
			}
			catch
			{
			}
			_challengeUnlock = map;
		}
		return _challengeUnlock.TryGetValue(girlId, out int value) ? value : 5;
	}

	/// <summary>该少女的无尽模式是否已解锁（18005 对应 bit）。</summary>
	internal static bool IsEndlessUnlocked(PlayerInstance player, int girlId)
	{
		if (girlId <= 0 || girlId >= 31)
		{
			return false;
		}
		uint bits = HouseAttr.Read(player, GirlEndlessStateSid);
		return ((bits >> girlId) & 1u) != 0;
	}

	/// <summary>普通模式累计回合（18010 + 少女ID）。</summary>
	internal static int NormalRounds(PlayerInstance player, int girlId)
	{
		if (girlId <= 0 || girlId >= 26)
		{
			return 0;
		}
		return (int)HouseAttr.Read(player, (uint)(GirlNormalStateSid + girlId));
	}

	/// <summary>按已有普通模式累计回合补齐 18005 位图（用于历史存档在修复前没置位的情况）。</summary>
	internal static async Task<int> RepairEndlessUnlockAsync(Connection connection, IEnumerable<int> girlIds, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		uint bits = HouseAttr.Read(player, GirlEndlessStateSid);
		uint before = bits;
		int added = 0;
		foreach (int girlId in girlIds)
		{
			if (girlId <= 0 || girlId >= 31)
			{
				continue;
			}
			if (((bits >> girlId) & 1u) != 0)
			{
				continue;
			}
			int need = ChallengeUnlockFor(girlId);
			if (NormalRounds(player, girlId) >= need)
			{
				bits |= (uint)(1 << girlId);
				added++;
			}
		}
		if (bits != before)
		{
			await HouseAttr.SetAsync(connection, GirlEndlessStateSid, bits, sync);
		}
		return added;
	}

	internal static bool IsGot(PlayerInstance player, int level)
	{
		if (level <= 0 || level > MaxRewardBit)
		{
			return false;
		}
		uint val = HouseAttr.Read(player, LevelRewardGotSid);
		return ((val >> level) & 1u) != 0;
	}

	internal static uint GotValue(PlayerInstance player, int level)
	{
		uint val = HouseAttr.Read(player, LevelRewardGotSid);
		if (level <= 0 || level > MaxRewardBit)
		{
			return val;
		}
		return val | (uint)(1 << level);
	}

	/// <summary>把奖励行转成客户端 Item.Gain 认的 5 元组数组。</summary>
	internal static JsonArray ToJson(List<uint>? reward)
	{
		JsonArray jsonArray = new JsonArray();
		if (reward == null)
		{
			return jsonArray;
		}
		foreach (uint value in reward)
		{
			jsonArray.Add(JsonValue.Create(value));
		}
		return jsonArray;
	}

	/// <summary>把等级奖励位图同步给客户端（界面据此把按钮刷成已领取）。</summary>
	internal static void SyncGot(PlayerInstance player, NtfSyncPlayer sync, uint value)
	{
		sync.Custom[player.ToPackedAttrKey(HouseAttr.Gid, LevelRewardGotSid)] = value;
		sync.Custom[player.ToShiftedAttrKey(HouseAttr.Gid, LevelRewardGotSid)] = value;
	}

	/// <summary>满级需要的累计经验；config 缺失时兜底 12000。</summary>
	internal static int FallbackMaxExp()
	{
		int max = MaxExp();
		return (max > 0) ? max : 12000;
	}
}
