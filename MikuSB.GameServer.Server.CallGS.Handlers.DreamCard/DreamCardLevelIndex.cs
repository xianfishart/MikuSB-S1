using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardLevelIndex
{
	private readonly HashSet<uint> ordinaryLevelIds;

	private readonly Dictionary<uint, DreamCardChallengeLevelEntry> challengeLevels;

	private readonly Dictionary<uint, DreamCardEndlessLevelEntry> endlessLevels;

	public DreamCardLevelIndex(IEnumerable<DreamCardOrdinaryLevelEntry> ordinaryLevels, IEnumerable<DreamCardChallengeLevelEntry> challengeLevels, IEnumerable<DreamCardEndlessLevelEntry> endlessLevels)
	{
		ordinaryLevelIds = (from x in ordinaryLevels
			where x.LevelListId != 0
			select x.LevelListId).ToHashSet();
		this.challengeLevels = (from x in challengeLevels
			where x.ChallengeId != 0
			group x by x.ChallengeId).ToDictionary((IGrouping<uint, DreamCardChallengeLevelEntry> x) => x.Key, (IGrouping<uint, DreamCardChallengeLevelEntry> x) => x.First());
		this.endlessLevels = (from x in endlessLevels
			where x.EndlessId != 0
			group x by x.EndlessId).ToDictionary((IGrouping<uint, DreamCardEndlessLevelEntry> x) => x.Key, (IGrouping<uint, DreamCardEndlessLevelEntry> x) => x.First());
	}

	public HashSet<uint> OpenOrdinaryLevelIds(DateTime now)
	{
		HashSet<uint> hashSet = new HashSet<uint>();
		foreach (DreamCardActivityExcel value in GameData.DreamCardActivityData.Values)
		{
			if (!IsActivityOpen(value, now))
			{
				continue;
			}
			foreach (uint item in value.LevelListID)
			{
				if (ordinaryLevelIds.Contains(item))
				{
					hashSet.Add(item);
				}
			}
		}
		return hashSet;
	}

	public bool IsChallengeOpen(uint id, DateTime now)
	{
		DreamCardChallengeLevelEntry value;
		return challengeLevels.TryGetValue(id, out value) && IsWithin(value.StartTime, value.EndTime, now);
	}

	public bool IsEndlessOpen(uint id, DateTime now)
	{
		DreamCardEndlessLevelEntry value;
		return endlessLevels.TryGetValue(id, out value) && IsWithin(value.StartTime, value.EndTime, now);
	}

	private static bool IsActivityOpen(DreamCardActivityExcel config, DateTime now)
	{
		DateTime? dateTime = ParseConfigTime(config.StartTime);
		if (!dateTime.HasValue || dateTime > now)
		{
			return false;
		}
		DateTime? dateTime2 = ParseConfigTime(config.EndTime);
		if (dateTime2.HasValue && now >= dateTime2.Value)
		{
			return false;
		}
		return string.IsNullOrWhiteSpace(config.Condition);
	}

	private static bool IsWithin(string? startRaw, string? endRaw, DateTime now)
	{
		DateTime? dateTime = ParseConfigTime(startRaw);
		if (!dateTime.HasValue || now < dateTime.Value)
		{
			return false;
		}
		DateTime? dateTime2 = ParseConfigTime(endRaw);
		return !dateTime2.HasValue || now < dateTime2.Value;
	}

	private static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}
}
