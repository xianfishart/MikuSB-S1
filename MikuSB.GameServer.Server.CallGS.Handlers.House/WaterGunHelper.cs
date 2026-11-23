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

/// <summary>
/// 心动水弹（LoveGun / 温泉中心水枪模式）的等级、经验、奖励领取口径。
/// 2026-11-12 新增（修复记录第 258 节）：把「一键全发」改成正常领取。
///
/// 客户端读的属性（运行时探针 S:GetHotSpringAttr(n) = 属性 sid 15000+n 实测）：
///   15011 = 当前等级（HouseHotSpring.GetWaterGunLevel）
///   15012 = 当前等级内的经验（GetWaterGunNowPoint）
///   15016 = 无尽模式最大回合（GetWaterGunMaxRounds）
/// 等级阈值来自 Resources/house/LoveGun/WaterGunRewards.json 的 Exp
/// （200,300,1000,1200,...,3700，共 15 级；客户端 HouseHotSpring.WaterGunLevelPoint 同一份）。
///
/// 领取标记沿用本项目其它小游戏的「位图」写法（HouseLevelRewardHelper 同款）：
///   sid = 15013 + level/30，bit = level % 30。1~15 级全落在 15013 里。
/// </summary>
internal static class WaterGunHelper
{
	/// <summary>当前等级。</summary>
	internal const uint LevelSid = 15011u;

	/// <summary>当前等级内经验。</summary>
	internal const uint PointSid = 15012u;

	/// <summary>等级奖励领取位图起始 sid。</summary>
	internal const uint StateSidBase = 15013u;

	/// <summary>水弹每局经验表的兜底值（ModeInfo.json 缺失时用）。</summary>
	internal const int FallbackExpPerRound = 100;

	internal struct Row
	{
		public int Level;

		public int Exp;

		public List<uint> Reward;
	}

	internal static List<Row> LoadRows()
	{
		List<Row> list = new List<Row>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "LoveGun", "WaterGunRewards.json");
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return list;
			}
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
					Exp = Math.Max(1, HouseJson.NumField(jsonObject, "Exp")),
					Reward = reward
				});
			}
		}
		catch
		{
		}
		list.Sort((Row a, Row b) => a.Level.CompareTo(b.Level));
		return list;
	}

	/// <summary>等级阈值表：下标即等级，thresholds[1] = 升到 2 级所需经验。</summary>
	internal static List<int> LoadThresholds()
	{
		List<int> list = new List<int> { 0, 200 };
		foreach (Row row in LoadRows())
		{
			while (list.Count <= row.Level)
			{
				list.Add(1);
			}
			list[row.Level] = Math.Max(1, row.Exp);
		}
		return list;
	}

	internal static int MaxLevel()
	{
		List<int> thresholds = LoadThresholds();
		return Math.Max(1, thresholds.Count - 1);
	}

	internal static int ReadLevel(PlayerInstance player)
	{
		return Math.Clamp((int)HouseAttr.Read(player, LevelSid), 1, MaxLevel());
	}

	internal static int ReadPoint(PlayerInstance player)
	{
		return Math.Max(0, (int)HouseAttr.Read(player, PointSid));
	}

	internal static int NeedExp(int level)
	{
		List<int> thresholds = LoadThresholds();
		if (level < 0 || level >= thresholds.Count)
		{
			return 0;
		}
		return thresholds[level];
	}

	internal static bool IsGot(PlayerInstance player, int level)
	{
		if (level <= 0)
		{
			return false;
		}
		uint sid = StateSidBase + (uint)(level / 30);
		int bit = level % 30;
		return ((HouseAttr.Read(player, sid) >> bit) & 1) != 0;
	}

	internal static async Task MarkGotAsync(Connection connection, int level, NtfSyncPlayer sync)
	{
		uint sid = StateSidBase + (uint)(level / 30);
		int bit = level % 30;
		uint val = HouseAttr.Read(connection.Player, sid) | (uint)(1 << bit);
		await HouseAttr.SetAsync(connection, sid, val, sync);
	}

	internal static List<uint> LoadReward(int level)
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

	/// <summary>按 ModeInfo.json 的 ExpPerRound 取每局经验；找不到返回兜底值。</summary>
	internal static int ExpPerRound(int modeType)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "LoveGun", "ModeInfo.json");
			if (File.Exists(path))
			{
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode node in jsonArray)
					{
						if (node is JsonObject jsonObject && HouseJson.NumField(jsonObject, "ModeType") == modeType)
						{
							int exp = HouseJson.NumField(jsonObject, "ExpPerRound");
							if (exp > 0)
							{
								return exp;
							}
						}
					}
				}
			}
		}
		catch
		{
		}
		return FallbackExpPerRound;
	}

	/// <summary>
	/// 连战（无尽，ModeType=2）每关包含的回合数，取 ModeInfo.json 的 RoundNum（当前 3）。
	/// 2026-11-12 追加（修复记录第 260 节）：客户端结算只传「已通关卡数」，
	/// 「最高回合」要的是回合数，换算必须靠这个常量。
	/// </summary>
	internal static int RoundsPerLevel()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "LoveGun", "ModeInfo.json");
			if (File.Exists(path))
			{
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode node in jsonArray)
					{
						if (node is JsonObject jsonObject && HouseJson.NumField(jsonObject, "ModeType") == 2)
						{
							int roundNum = HouseJson.NumField(jsonObject, "RoundNum");
							if (roundNum > 0)
							{
								return roundNum;
							}
						}
					}
				}
			}
		}
		catch
		{
		}
		return 3;
	}

	/// <summary>结算请求里 RoundsState 数组中「赢」的回合数（元素为 1 的个数）。</summary>
	internal static int CountWins(JsonNode? roundsState)
	{
		if (!(roundsState is JsonArray jsonArray))
		{
			return 0;
		}
		int num = 0;
		foreach (JsonNode item in jsonArray)
		{
			if (HouseJson.ToInt(item) == 1)
			{
				num++;
			}
		}
		return num;
	}

	/// <summary>
	/// 连战模式的「最高回合」口径（修复记录第 260 节）：
	///   · 通关（IsSuccess=0）：Rounds = 已通关卡数，本关 3 回合全赢，所以回合数 = Rounds × 每关回合数；
	///   · 失败（IsSuccess=2）：Rounds = 通关卡数，本关只赢了一部分，回合数 = Rounds × 每关回合数 + RoundsState 里的胜场。
	/// 实测样本（2026-11-12 20:30，玩家清掉第 1 关）：
	///   {"ModeType":2,"IsSuccess":0,"Rounds":1,"RoundsState":[1,1,1]} → 该记 3，旧逻辑记的是 1。
	/// </summary>
	internal static int ScoreFromEnd(int rounds, int isSuccess, JsonNode? roundsState)
	{
		int perLevel = RoundsPerLevel();
		long score = (long)Math.Max(0, rounds) * perLevel;
		if (isSuccess != 0)
		{
			score += CountWins(roundsState);
		}
		return (int)Math.Clamp(score, 0, int.MaxValue);
	}

	/// <summary>加经验并结算升级，返回 (addExp, newLevel, newPoint, needExp)。</summary>
	internal static async Task<(int AddExp, int Level, int Point, int Need)> AddExpAsync(Connection connection, int addExp, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		List<int> thresholds = LoadThresholds();
		int maxLevel = Math.Max(1, thresholds.Count - 1);
		int level = Math.Clamp((int)HouseAttr.Read(player, LevelSid), 1, maxLevel);
		int point = ReadPoint(player) + Math.Max(0, addExp);
		while (level < maxLevel && point >= thresholds[level])
		{
			point -= thresholds[level];
			level++;
		}
		if (level >= maxLevel)
		{
			level = maxLevel;
			point = Math.Clamp(point, 0, Math.Max(0, thresholds[level] - 1));
		}
		await HouseAttr.SetAsync(connection, LevelSid, (uint)level, sync);
		await HouseAttr.SetAsync(connection, PointSid, (uint)Math.Max(0, point), sync);
		return (AddExp: Math.Max(0, addExp), Level: level, Point: point, Need: NeedExp(level));
	}

	/// <summary>发一级奖励：发道具 + 打标记 + 存档。返回发的奖励列表（已领过则返回 null）。</summary>
	internal static async Task<List<uint>> GrantAsync(Connection connection, int level, NtfSyncPlayer sync)
	{
		if (level <= 0 || IsGot(connection.Player, level))
		{
			return null;
		}
		List<uint> reward = LoadReward(level);
		await MarkGotAsync(connection, level, sync);
		if (reward != null && reward.Count >= 5)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(connection.Player, sync, reward);
		}
		DatabaseHelper.SaveDatabaseType(connection.Player.Data);
		DatabaseHelper.SaveDatabaseType(connection.Player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(connection.Player.CharacterManager.CharacterData);
		return reward;
	}

	/// <summary>把奖励行转成 JsonArray（客户端要的是平铺数字数组）。</summary>
	internal static JsonArray ToJson(List<uint> reward)
	{
		JsonArray jsonArray = new JsonArray();
		if (reward == null)
		{
			return jsonArray;
		}
		foreach (uint item in reward)
		{
			jsonArray.Add(JsonValue.Create(item));
		}
		return jsonArray;
	}

	/// <summary>已领过的等级列表（升序）。</summary>
	internal static JsonArray GotLevels(int upTo)
	{
		JsonArray jsonArray = new JsonArray();
		for (int i = 1; i <= upTo; i++)
		{
			jsonArray.Add(JsonValue.Create(i));
		}
		return jsonArray;
	}

	// -----------------------------------------------------------------------------------------
	// 成就（任务）部分：2026-11-12 追加（修复记录第 264 节）。
	// 水弹成就 = Resources/achievement/activity_quest.json 里 ID 20101~20120、Type 264~276 的 20 条，
	// 进度存在 Gid=7（GroupID_Quest）/ Sid=成就 ID 上，Val = (进度 << 1) | 已领标记。
	// 客户端 CompleteAchievement 只按 AchType 单类型上报，一键解锁要遍历全部 20 条。
	// -----------------------------------------------------------------------------------------

	/// <summary>成就进度属性所在的 Gid（GroupID_Quest）。</summary>
	internal const uint AchGid = 7u;

	internal const int AchMinId = 20100;

	internal const int AchMaxId = 20199;

	internal const int AchMinType = 264;

	internal const int AchMaxType = 276;

	/// <summary>读水弹成就表：返回 (成就 ID, 目标进度 CheckValue) 升序列表。</summary>
	internal static List<KeyValuePair<int, int>> LoadAchRows()
	{
		List<KeyValuePair<int, int>> list = new List<KeyValuePair<int, int>>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (rows == null)
			{
				return list;
			}
			foreach (JsonNode node in rows)
			{
				if (!(node is JsonObject obj))
				{
					continue;
				}
				int id = HouseJson.NumField(obj, "ID");
				int type = HouseJson.NumField(obj, "Type");
				if (id < AchMinId || id > AchMaxId || type < AchMinType || type > AchMaxType)
				{
					continue;
				}
				list.Add(new KeyValuePair<int, int>(id, Math.Max(1, HouseJson.NumField(obj, "CheckValue"))));
			}
		}
		catch
		{
		}
		list.Sort((KeyValuePair<int, int> a, KeyValuePair<int, int> b) => a.Key.CompareTo(b.Key));
		return list;
	}

	/// <summary>
	/// 把 20 条水弹成就的进度全部写满，**保留最低位的「已领」标记**（只解锁不解领）。
	/// 返回 (总条数, 本次新达成, 原本已达成)。
	/// </summary>
	internal static (int Total, int Newly, int Already) UnlockAllAch(PlayerInstance player, NtfSyncPlayer sync)
	{
		int total = 0;
		int newly = 0;
		int already = 0;
		foreach (KeyValuePair<int, int> row in LoadAchRows())
		{
			total++;
			uint sid = (uint)row.Key;
			uint target = (row.Value > 0 ? (uint)row.Value : 1u) << 1;
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == AchGid && x.Sid == sid);
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = AchGid,
					Sid = sid,
					Val = 0u
				};
				player.Data.Attrs.Add(attr);
			}
			uint val = (attr.Val & 1u) | target;
			if (val == attr.Val)
			{
				already++;
			}
			else
			{
				newly++;
			}
			attr.Val = val;
			sync.Custom[player.ToPackedAttrKey(AchGid, sid)] = val;
			sync.Custom[player.ToShiftedAttrKey(AchGid, sid)] = val;
		}
		if (total > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return (Total: total, Newly: newly, Already: already);
	}
}
