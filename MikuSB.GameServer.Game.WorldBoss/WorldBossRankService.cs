using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.WorldBoss;

/// <summary>
/// 「冬日接力战」周期最佳阵容排行（RankName = WorldBoss_Rank_&lt;ReferRank1&gt;）—— 第 442 节。
///
/// 客户端 <c>WorldBossLogic.GetRankList(nReferRank1)</c> 会发 <c>ReqRankList{rank_name="WorldBoss_Rank_1001"}</c>，
/// 服务端原先只会回一个空 <c>RankList</c>，于是界面上永远是「暂未上榜」且排行列表是空的。
///
/// 这里做「本服单机榜」：把每个玩家本期的最高伤害记下来，按分数倒序回给客户端。
/// 数据落在 <c>Config/Database/WorldBossRank.json</c>，按榜单名（也就是期数）分组，重启不丢。
/// </summary>
public sealed class WorldBossRankEntry
{
	public ulong Uid { get; set; }

	public string Name { get; set; } = string.Empty;

	/// <summary>阵容标识串（客户端 GetLineupIdentifier 的格式：每个成员 "DDDPP"，按字符串排序后拼接）。</summary>
	public string Lineup { get; set; } = string.Empty;

	public uint Score { get; set; }

	/// <summary>打成这个分数的时刻（Unix 秒），同分时先到先排。</summary>
	public long Time { get; set; }
}

public static class WorldBossRankService
{
	public const int DefaultMax = 10;

	private static readonly Logger Log = new Logger("WorldBossRank");

	private static readonly object Gate = new object();

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private static Dictionary<string, List<WorldBossRankEntry>>? _boards;

	public static string RankNameOf(uint referRank1)
	{
		return $"WorldBoss_Rank_{referRank1}";
	}

	public static bool IsWorldBossRank(string? rankName)
	{
		return !string.IsNullOrEmpty(rankName) && rankName!.StartsWith("WorldBoss_Rank", StringComparison.Ordinal);
	}

	private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Config", "Database", "WorldBossRank.json");

	private static Dictionary<string, List<WorldBossRankEntry>> Load()
	{
		if (_boards != null)
		{
			return _boards;
		}
		try
		{
			if (File.Exists(FilePath))
			{
				string json = File.ReadAllText(FilePath);
				_boards = JsonSerializer.Deserialize<Dictionary<string, List<WorldBossRankEntry>>>(json) ?? new Dictionary<string, List<WorldBossRankEntry>>();
			}
			else
			{
				_boards = new Dictionary<string, List<WorldBossRankEntry>>();
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"读取排行榜失败：{ex.GetType().Name}: {ex.Message}");
			_boards = new Dictionary<string, List<WorldBossRankEntry>>();
		}
		return _boards;
	}

	private static void Save(Dictionary<string, List<WorldBossRankEntry>> boards)
	{
		try
		{
			string? dir = Path.GetDirectoryName(FilePath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.WriteAllText(FilePath, JsonSerializer.Serialize(boards, JsonOptions));
		}
		catch (Exception ex)
		{
			Log.Warn($"写入排行榜失败：{ex.GetType().Name}: {ex.Message}");
		}
	}

	/// <summary>记录一次成绩（只在刷新最高分时覆盖分数与阵容）。</summary>
	public static void Record(string rankName, ulong uid, string? name, string? lineup, uint score, long time)
	{
		if (string.IsNullOrEmpty(rankName) || score == 0 || uid == 0)
		{
			return;
		}
		lock (Gate)
		{
			Dictionary<string, List<WorldBossRankEntry>> boards = Load();
			if (!boards.TryGetValue(rankName, out List<WorldBossRankEntry>? list) || list == null)
			{
				list = new List<WorldBossRankEntry>();
				boards[rankName] = list;
			}
			WorldBossRankEntry? entry = list.FirstOrDefault((WorldBossRankEntry x) => x.Uid == uid);
			if (entry == null)
			{
				entry = new WorldBossRankEntry
				{
					Uid = uid
				};
				list.Add(entry);
			}
			bool improved = score > entry.Score;
			if (improved)
			{
				entry.Score = score;
				entry.Time = time;
				if (!string.IsNullOrEmpty(lineup))
				{
					entry.Lineup = lineup!;
				}
			}
			if (!string.IsNullOrEmpty(name))
			{
				entry.Name = name!;
			}
			if (improved || list.Count == 0)
			{
				Save(boards);
				Log.Info($"榜单 {rankName} 更新：uid={uid} name={entry.Name} score={entry.Score} lineup={entry.Lineup}");
			}
			else
			{
				// 名字变了也顺手落一次盘，避免重启后叫回旧名字。
				Save(boards);
			}
		}
	}

	/// <summary>取榜单前 max 名（分数倒序，同分先到先排）。</summary>
	public static List<WorldBossRankEntry> Top(string rankName, int max)
	{
		if (string.IsNullOrEmpty(rankName))
		{
			return new List<WorldBossRankEntry>();
		}
		if (max <= 0)
		{
			max = DefaultMax;
		}
		lock (Gate)
		{
			Dictionary<string, List<WorldBossRankEntry>> boards = Load();
			if (!boards.TryGetValue(rankName, out List<WorldBossRankEntry>? list) || list == null)
			{
				return new List<WorldBossRankEntry>();
			}
			return list.Where((WorldBossRankEntry x) => x.Score > 0)
				.OrderByDescending((WorldBossRankEntry x) => x.Score)
				.ThenBy((WorldBossRankEntry x) => x.Time)
				.Take(max)
				.ToList();
		}
	}
}
