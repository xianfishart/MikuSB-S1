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
/// 2026-11-13 新增（修复记录第 272 节）：神经调理（疗愈中心）的服务端口径助手。
///
/// 客户端反汇编确认的属性布局（HouseStorage 常量）：
///   HouseMassageRoomInfoStart = 10000
///     +1  HouseMassageLevel            等级（0 视为 1）
///     +2  HouseMassageLevelNowExp      当前级内经验
///     +3  HouseMassageLevelRewardStart 等级奖励位图起点（第 level%30 位，块 = level/30）
///     +6  HouseMassageDiff             难度相关
///   MassageGirlInfoStart = 11000，GirlAttrCount = 50
///     +（24 + 难度）  第 0~2 位 = 该难度历史最高星，bit3 起 = 调理次数
///                      取值：GetGirlHardLevelMassageTimes = GetBits(attr, 3, 30)
///     +（31 + 难度）  疗愈评级（星级展示）AllStars 直接读整个值
///     +38             五星达成奖励领取位图：第 hardLevel 位（1/2/3）为 1 表示已领
///     +39             已解锁最高难度
///
/// 旧实现把「星级」写在 31+难度、把「次数」写在 24+难度 但低 3 位一直是 0，
/// 而客户端的五星奖励可领判定读的正是 24+难度 的低 3 位 → 永远判不出来 → 点了没反应。
/// 经验（10002）则从来没被写过，所以等级页一直 0/400。
/// </summary>
internal static class HouseMassageHelper
{
	internal const uint LevelSid = 10001u;

	internal const uint ExpSid = 10002u;

	/// <summary>补发经验的一次性标记位（10007~10009 客户端未使用，取 9 避开 6=Diff、10+=家具）。</summary>
	internal const uint ExpRepairMarkSid = 10009u;

	internal const int MaxLevel = 15;

	internal const int StarMask = 7;

	internal const int TimesShift = 3;

	private const string LogPath = "D:/DSH/.tools/massage_fix.log";

	private static List<Row>? _levels;

	private static int[]? _addExp;

	private sealed class Row
	{
		public int Level;

		public int Exp;

		/// <summary>该等级的奖励条目，每条是 [类型, id, ?, ?, 数量]。</summary>
		public List<List<uint>> Items = new List<List<uint>>();
	}

	internal static void Log(string msg)
	{
		try
		{
			string text = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + Environment.NewLine;
			File.AppendAllText(LogPath, text);
		}
		catch
		{
		}
	}

	/// <summary>读 Resources/house/massage/massage_level_reward.json（Level / Exp / Item）。</summary>
	private static List<Row> Levels()
	{
		if (_levels != null)
		{
			return _levels;
		}
		List<Row> list = new List<Row>();
		try
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house");
			JsonArray? jsonArray = null;
			foreach (string candidate in new string[2]
			{
				Path.Combine(root, "massage", "massage_level_reward.json"),
				Path.Combine(root, "massage_level_reward.json")
			})
			{
				if (File.Exists(candidate))
				{
					jsonArray = JsonNode.Parse(File.ReadAllText(candidate))?.AsArray();
					if (jsonArray != null)
					{
						break;
					}
				}
			}
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (!(item is JsonObject jsonObject))
					{
						continue;
					}
					Row row = new Row
					{
						Level = HouseJson.NumField(jsonObject, "Level"),
						Exp = HouseJson.NumField(jsonObject, "Exp")
					};
					JsonNode? node = jsonObject["Item"] ?? jsonObject["Rows"];
					if (node is JsonArray array && array.Count > 0)
					{
						if (array[0] is JsonArray)
						{
							foreach (JsonNode entry in array)
							{
								if (entry is JsonArray itemRow)
								{
									row.Items.Add(itemRow.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).ToList());
								}
							}
						}
						else
						{
							row.Items.Add(array.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).ToList());
						}
					}
					list.Add(row);
				}
			}
		}
		catch (Exception ex)
		{
			Log("load level reward fail: " + ex.Message);
		}
		_levels = list;
		return list;
	}

	/// <summary>升到「下一级」所需经验；超出配置按 1000000 兜底。</summary>
	internal static int LevelUpNeed(int level)
	{
		Row? row = Levels().FirstOrDefault((Row x) => x.Level == level);
		if (row == null || row.Exp <= 0)
		{
			return 1000000;
		}
		return row.Exp;
	}

	internal static int LevelRewardCount()
	{
		return Levels().Count;
	}

	internal static List<uint>? LevelRewardItem(int level)
	{
		return Levels().FirstOrDefault((Row x) => x.Level == level)?.Items.FirstOrDefault();
	}

	/// <summary>等级奖励位图所在 Sid：10000 + 3 + level/30（客户端 IsMassageLevelRewardGot 同口径）。</summary>
	internal static uint LevelRewardStartSid(int level)
	{
		return (uint)(10003 + level / 30);
	}

	/// <summary>某等级的全部奖励条目（配置里一个等级可能给多件）。</summary>
	internal static List<List<uint>> LevelRewardItems(int level)
	{
		return Levels().FirstOrDefault((Row x) => x.Level == level)?.Items ?? new List<List<uint>>();
	}

	/// <summary>读取 massage_difficulty.json 的 AddExp[星级-1]。</summary>
	internal static int AddExpFor(int hardLevel, int star)
	{
		if (_addExp == null)
		{
			// 2026-11-13 fix：下标是 hard * 8 + index，hard 最大 3 → 31，
			// 原来开 24 个槽会在困难难度（24~28）直接越界抛异常并中断整张表。
			int[] table = new int[32];
			try
			{
				string root = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house");
				JsonArray? jsonArray = null;
				foreach (string candidate in new string[2]
				{
					Path.Combine(root, "massage", "massage_difficulty.json"),
					Path.Combine(root, "massage_difficulty.json")
				})
				{
					if (File.Exists(candidate))
					{
						jsonArray = JsonNode.Parse(File.ReadAllText(candidate))?.AsArray();
						if (jsonArray != null)
						{
							break;
						}
					}
				}
				if (jsonArray != null)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (!(item is JsonObject jsonObject))
						{
							continue;
						}
						int diff = HouseJson.NumField(jsonObject, "Difficulty");
						if (diff < 1 || diff > 3 || !(jsonObject["AddExp"] is JsonArray addExp))
						{
							continue;
						}
						for (int i = 0; i < addExp.Count && i < 5; i++)
						{
							table[diff * 8 + i] = HouseJson.ToInt(addExp[i]);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log("load difficulty fail: " + ex.Message);
			}
			_addExp = table;
		}
		int index = Math.Clamp(star, 1, 5) - 1;
		int hard = Math.Clamp(hardLevel, 1, 3);
		return _addExp[hard * 8 + index];
	}

	/// <summary>可调理的少女 ID 列表（来自 massage_girls.json，避免硬编码漏人）。</summary>
	internal static int[] GirlIds()
	{
		try
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house");
			JsonArray? jsonArray = null;
			foreach (string candidate in new string[2]
			{
				Path.Combine(root, "massage", "massage_girls.json"),
				Path.Combine(root, "massage_girls.json")
			})
			{
				if (File.Exists(candidate))
				{
					jsonArray = JsonNode.Parse(File.ReadAllText(candidate))?.AsArray();
					if (jsonArray != null)
					{
						break;
					}
				}
			}
			if (jsonArray == null)
			{
				return Array.Empty<int>();
			}
			return (from JsonNode node in jsonArray
				where node is JsonObject
				select HouseJson.NumField((JsonObject)node, "GirlId") into id
				where id > 0
				select id).Distinct().ToArray();
		}
		catch (Exception ex)
		{
			Log("load girl ids fail: " + ex.Message);
			return Array.Empty<int>();
		}
	}

	/// <summary>读少女某难度的五星奖励（massage_girls.json 的 Reward1/2/3）。</summary>
	internal static List<uint>? GirlReward(int girlId, int hardLevel)
	{
		try
		{
			string root = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house");
			JsonArray? jsonArray = null;
			foreach (string candidate in new string[2]
			{
				Path.Combine(root, "massage", "massage_girls.json"),
				Path.Combine(root, "massage_girls.json")
			})
			{
				if (File.Exists(candidate))
				{
					jsonArray = JsonNode.Parse(File.ReadAllText(candidate))?.AsArray();
					if (jsonArray != null)
					{
						break;
					}
				}
			}
			if (jsonArray == null)
			{
				return null;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || HouseJson.NumField(jsonObject, "GirlId") != girlId)
				{
					continue;
				}
				if (jsonObject["Reward" + hardLevel] is JsonArray source)
				{
					return source.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).ToList();
				}
			}
		}
		catch (Exception ex)
		{
			Log("load girl reward fail: " + ex.Message);
		}
		return null;
	}

	internal static int Level(PlayerInstance player)
	{
		int value = (int)player.Attributes.GetValue(101u, LevelSid);
		return Math.Clamp(value, 1, MaxLevel);
	}

	internal static int Exp(PlayerInstance player)
	{
		return (int)player.Attributes.GetValue(101u, ExpSid);
	}

	internal static int StarsInTimesField(PlayerInstance player, int girlId, int hardLevel)
	{
		uint value = HouseMassageAttr.Read(player, girlId, HouseMassageAttr.TimesOffset(hardLevel));
		return (int)(value & StarMask);
	}

	internal static void SetStarsInTimesField(PlayerInstance player, NtfSyncPlayer sync, int girlId, int hardLevel, int star)
	{
		int offset = HouseMassageAttr.TimesOffset(hardLevel);
		uint value = HouseMassageAttr.Read(player, girlId, offset);
		int old = (int)(value & StarMask);
		if (star <= old)
		{
			return;
		}
		uint merged = (value & ~(uint)StarMask) | (uint)Math.Clamp(star, 0, StarMask);
		HouseMassageAttr.Set(player, sync, girlId, offset, merged);
	}

	/// <summary>加经验并处理升级（经验阈值来自 massage_level_reward.json 的 Exp）。返回 (新等级, 新经验, 实加)。</summary>
	internal static (int Level, int Exp, int Added) AddExp(PlayerInstance player, NtfSyncPlayer sync, int add)
	{
		int level = Level(player);
		int exp = Exp(player);
		if (add <= 0)
		{
			return (level, exp, 0);
		}
		exp += add;
		while (level < MaxLevel)
		{
			int need = LevelUpNeed(level);
			if (exp < need)
			{
				break;
			}
			exp -= need;
			level++;
			Log($"level up -> {level} (need was {need}), exp left {exp}");
		}
		if (level >= MaxLevel)
		{
			level = MaxLevel;
			int max = LevelUpNeed(MaxLevel);
			if (exp > max)
			{
				exp = max;
			}
		}
		player.Attributes.Set(101u, LevelSid, (uint)level);
		player.Attributes.SyncTo(sync, 101u, LevelSid, (uint)level);
		player.Attributes.Set(101u, ExpSid, (uint)exp);
		player.Attributes.SyncTo(sync, 101u, ExpSid, (uint)exp);
		return (level, exp, add);
	}

	/// <summary>五星达成奖励发奖（按 hardLevel 的第 hardLevel 位置已领标记）。</summary>
	internal static async Task<bool> GrantStarRewardAsync(Connection connection, NtfSyncPlayer sync, int girlId, int hardLevel, bool force)
	{
		PlayerInstance player = connection.Player;
		if (player == null || girlId <= 0 || hardLevel < 1 || hardLevel > 3)
		{
			return false;
		}
		List<uint>? reward = GirlReward(girlId, hardLevel);
		if (reward == null || reward.Count == 0)
		{
			Log($"grant star reward: no config girl={girlId} hard={hardLevel}");
			return false;
		}
		uint sid = HouseMassageAttr.Sid(girlId, HouseMassageAttr.HardLevelFiveStarsReward);
		uint value = player.Attributes.GetValue(101u, sid);
		int bit = hardLevel;
		if (!force && ((value >> bit) & 1) == 1)
		{
			return false;
		}
		value |= (uint)(1 << bit);
		player.Attributes.Set(101u, sid, value);
		player.Attributes.SyncTo(sync, 101u, sid, value);
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = value;
		await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		Log($"grant star reward girl={girlId} hard={hardLevel} reward=[{string.Join(",", reward)}]");
		return true;
	}

	/// <summary>
	/// 老存档修复：把「疗愈评级」（31+难度）补写进 24+难度 的低 3 位、
	/// 补发已达成五星的奖励、并按历史次数补算一次经验（用 10009 标记，只补一次）。
	/// </summary>
	internal static async Task<string> RepairAsync(Connection connection, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return "no player";
		}
		int[] girls = GirlIds();
		int fixedStars = 0;
		int granted = 0;
		int expSum = 0;
		foreach (int girlId in girls)
		{
			for (int hard = 1; hard <= 3; hard++)
			{
				int history = (int)HouseMassageAttr.Read(player, girlId, HouseMassageAttr.StarsOffset(hard));
				if (history > 0 && history <= 5)
				{
					int before = StarsInTimesField(player, girlId, hard);
					if (history > before)
					{
						SetStarsInTimesField(player, sync, girlId, hard, history);
						fixedStars++;
					}
				}
				int stars = Math.Max(history, StarsInTimesField(player, girlId, hard));
				uint timesValue = HouseMassageAttr.Read(player, girlId, HouseMassageAttr.TimesOffset(hard));
				int times = (int)(timesValue >> TimesShift);
				if (stars >= 5)
				{
					if (await GrantStarRewardAsync(connection, sync, girlId, hard, force: false))
					{
						granted++;
					}
				}
				if (times > 0 && stars > 0)
				{
					expSum += times * AddExpFor(hard, stars);
				}
			}
		}
		bool expRepaired = player.Attributes.GetValue(101u, ExpRepairMarkSid) != 0;
		int added = 0;
		if (!expRepaired && expSum > 0)
		{
			(int Level, int Exp, int Added) tuple = AddExp(player, sync, expSum);
			added = tuple.Added;
			player.Attributes.Set(101u, ExpRepairMarkSid, 1u);
			player.Attributes.SyncTo(sync, 101u, ExpRepairMarkSid, 1u);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		string result = $"starsFixed={fixedStars} rewardGranted={granted} expAdded={added} expComp={expSum} lv={Level(player)} exp={Exp(player)}";
		Log("repair uid=" + player.Uid + " " + result);
		return result;
	}
}
