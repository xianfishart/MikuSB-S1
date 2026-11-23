using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

/// <summary>
/// 异绪消解（GirlRush）关卡表的轻量读取：只取 ID -> ReferChar，用于兜底默认角色。
/// 客户端把「本关选的角色/武器/技能」存在属性组 158、sid = 关卡 ID 的位域里，
/// 若该属性缺失（玩家没走保存流程），进关卡会读到角色 0 -> GirlPath invalid -> 卡死。
///
/// 进度侧与客户端 Lua 完全对齐（见 GirlRush.GirlRushLevelCfg:GetPassTime / GetHistoryLevel）：
///   属性组 22  sid = 关卡ID，val = 通关用时（秒），> 0 即视为已通关（Launch.GPASSID）
///   属性组 158 sid = 关卡ID，bit 26..30 = 历史评价等级（越小越好，1 = 极）
///   属性组 160 sid = 奖励 Index，bit0 = 已领取
/// </summary>
internal static class GirlRushStore
{
	/// <summary>关卡选择位域（角色/武器/技能/火力 + 历史评价）。</summary>
	internal const uint AttrGid = 158u;

	/// <summary>通关用时（秒）。客户端 Launch.GPASSID = 22。</summary>
	internal const uint PassGid = 22u;

	/// <summary>心绪补给领取标记，sid = 奖励 Index。</summary>
	internal const uint AwardGid = 160u;

	/// <summary>历史评价在 158 位域里的起始位。</summary>
	internal const int RatingShift = 26;

	private const uint RatingMask = 0x1Fu;

	/// <summary>158 位域低 26 位是编队信息，写评价时必须原样保留。</summary>
	private const uint LineupMask = 0x03FFFFFFu;

	private static Dictionary<uint, uint>? _referChars;

	private static List<GirlRushAward>? _awards;

	/// <summary>uid -> (关卡, 进关时刻)，用于结算时算通关用时。</summary>
	private static readonly ConcurrentDictionary<int, (uint LevelId, DateTime At)> Entries = new ConcurrentDictionary<int, (uint, DateTime)>();

	internal sealed class GirlRushAward
	{
		public uint Index { get; init; }

		public uint LevelId { get; init; }

		public List<List<uint>> Conditions { get; init; } = new List<List<uint>>();

		public List<List<uint>> Items { get; init; } = new List<List<uint>>();
	}

	/// <summary>进关时打点，结算时据此计算通关用时。</summary>
	public static void MarkEnter(int uid, uint levelId)
	{
		Entries[uid] = (levelId, DateTime.UtcNow);
	}

	/// <summary>调试日志（和 EnterLevel/SaveData 共用一个文件）。</summary>
	public static void AppendDebug(string line)
	{
		try
		{
			File.AppendAllText(@"Config/Logs/girlrush_dbg.txt", line + "\n");
		}
		catch
		{
		}
	}

	/// <summary>
	/// 取出本次通关用时（秒）。没有进关打点时回 1 秒——真实结算只可能来自一次真实进关，
	/// 服务端中途重启丢掉打点时不该把玩家判成“未通关”。
	/// </summary>
	public static uint TakePassTime(int uid, uint levelId)
	{
		if (Entries.TryRemove(uid, out (uint LevelId, DateTime At) entry) && entry.LevelId == levelId)
		{
			double seconds = (DateTime.UtcNow - entry.At).TotalSeconds;
			if (seconds < 1.0)
			{
				seconds = 1.0;
			}
			return (uint)Math.Min(seconds, 86400.0);
		}
		return 1u;
	}

	/// <summary>写入通关用时（保留更好成绩：用时更短）。</summary>
	public static void WritePassTime(PlayerInstance player, uint levelId, uint seconds)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(PassGid, levelId);
		if (attr.Val == 0 || (seconds != 0 && seconds < attr.Val))
		{
			attr.Val = seconds;
		}
	}

	/// <summary>写入历史评价等级（保留更好成绩：数值更小）。</summary>
	public static void WriteRating(PlayerInstance player, uint levelId, uint rating)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(AttrGid, levelId);
		uint current = (attr.Val >> RatingShift) & RatingMask;
		uint value = rating & RatingMask;
		if (current != 0 && (value == 0 || current <= value))
		{
			value = current;
		}
		attr.Val = (attr.Val & LineupMask) | (value << RatingShift);
	}

	/// <summary>按奖励序号找配置（girlrushaward.json）。</summary>
	public static GirlRushAward? FindAward(uint index)
	{
		foreach (GirlRushAward award in Awards())
		{
			if (award.Index == index)
			{
				return award;
			}
		}
		return null;
	}

	/// <summary>
	/// 条件判定，与客户端 CheckLockConditionFunc 一一对应：
	///   type 1 = [1, 关卡ID, 最短用时]   -> passTime >= 最短用时
	///   type 2 = [2, 关卡ID, 评价上限]   -> 已通关 且 历史评价 &lt;= 上限
	/// </summary>
	public static bool CheckAwardCondition(PlayerInstance player, GirlRushAward award)
	{
		foreach (List<uint> condition in award.Conditions)
		{
			if (condition.Count < 3)
			{
				continue;
			}
			uint type = condition[0];
			uint levelId = condition[1];
			uint param = condition[2];
			uint passTime = player.Attributes.GetValue(PassGid, levelId);
			if (passTime == 0)
			{
				return false;
			}
			if (type == 1)
			{
				if (passTime < param)
				{
					return false;
				}
				continue;
			}
			if (type == 2)
			{
				uint rating = (player.Attributes.GetValue(AttrGid, levelId) >> RatingShift) & RatingMask;
				if (rating == 0 || rating > param)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static List<GirlRushAward> Awards()
	{
		if (_awards != null)
		{
			return _awards;
		}
		List<GirlRushAward> list = new List<GirlRushAward>();
		_awards = list;
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "girlrush", "girlrushaward.json");
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray rows = JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray();
			foreach (JsonNode? node in rows)
			{
				if (node is not JsonObject row)
				{
					continue;
				}
				list.Add(new GirlRushAward
				{
					Index = ReadUint(row["Index"]),
					LevelId = ReadUint(row["Level"]),
					Conditions = ReadTuples(row["Condition"]),
					Items = ReadTuples(row["Award"])
				});
			}
		}
		catch
		{
		}
		return list;
	}

	private static List<List<uint>> ReadTuples(JsonNode? node)
	{
		List<List<uint>> result = new List<List<uint>>();
		if (node is not JsonArray rows)
		{
			return result;
		}
		foreach (JsonNode? row in rows)
		{
			if (row is JsonArray cells)
			{
				result.Add(cells.Select(ReadUint).ToList());
			}
		}
		return result;
	}

	/// <summary>该关卡默认角色（girlrushlevel.json 的 ReferChar），找不到时回 1。</summary>
	public static uint DefaultRoleId(uint levelId)
	{
		Dictionary<uint, uint> table = ReferChars();
		if (table.TryGetValue(levelId, out uint role) && role != 0)
		{
			return role;
		}
		return 1u;
	}

	private static Dictionary<uint, uint> ReferChars()
	{
		if (_referChars != null)
		{
			return _referChars;
		}
		_referChars = new Dictionary<uint, uint>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "girlrush", "girlrushlevel.json");
			if (!File.Exists(path))
			{
				return _referChars;
			}
			foreach (JsonNode? item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (item is JsonObject row)
				{
					uint id = ReadUint(row["ID"]);
					if (id != 0)
					{
						_referChars[id] = ReadUint(row["ReferChar"]);
					}
				}
			}
		}
		catch
		{
		}
		return _referChars;
	}

	public static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue value && value.TryGetValue<uint>(out uint number))
		{
			return number;
		}
		return uint.TryParse(node?.ToString(), out uint parsed) ? parsed : 0u;
	}
}
