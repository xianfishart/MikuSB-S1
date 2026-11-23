using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardSettlementIndex
{
	private readonly Dictionary<(uint LevelId, uint Diff), uint> ordinaryRewardIds;

	private readonly List<DreamCardChallengeTimeEntry> challengeTimes;

	public DreamCardSettlementIndex(IEnumerable<DreamCardOrdinarySettlementEntry> ordinaryLevels, IEnumerable<DreamCardChallengeTimeEntry> challengeTimes)
	{
		ordinaryRewardIds = (from x in ordinaryLevels
			where x.LevelListId != 0 && x.HardStage != 0
			group x by (LevelListId: x.LevelListId, HardStage: x.HardStage)).ToDictionary((IGrouping<(uint LevelListId, uint HardStage), DreamCardOrdinarySettlementEntry> x) => x.Key, (IGrouping<(uint LevelListId, uint HardStage), DreamCardOrdinarySettlementEntry> x) => x.First().RewardId);
		this.challengeTimes = challengeTimes.ToList();
	}

	public bool TryGetOrdinaryRewardId(uint levelId, uint diff, out uint rewardId)
	{
		return ordinaryRewardIds.TryGetValue((levelId, diff), out rewardId);
	}

	public uint ResolveCurrentChallengePeriodId(DateTime now)
	{
		foreach (DreamCardChallengeTimeEntry item in challengeTimes.OrderBy((DreamCardChallengeTimeEntry x) => x.ChallTimeId))
		{
			DateTime? dateTime = ParseConfigTime(item.StartTime);
			DateTime? dateTime2 = ParseConfigTime(item.EndTime);
			if (!dateTime.HasValue || !dateTime2.HasValue || !(dateTime.Value <= now) || !(now < dateTime2.Value))
			{
				continue;
			}
			return item.ChallTimeId;
		}
		// 第 494 节：配置里所有挑战期都过期时，回落到最后一期（只要它已经开始）。
		// 否则「纷彩世界」页签永远 bChallOpen=false、点一下弹「暂未开放」，
		// 挑战奖励树也永远打不开。真正的开关由 chall_time.json 的时间窗决定，
		// 这里只兜底「配置写死了过去时间」这一种情况。
		if (challengeTimes.Count == 0)
		{
			return 0u;
		}
		DreamCardChallengeTimeEntry last = challengeTimes.OrderBy((DreamCardChallengeTimeEntry x) => x.ChallTimeId).Last();
		DateTime? lastStart = ParseConfigTime(last.StartTime);
		if (lastStart.HasValue && lastStart.Value <= now)
		{
			return last.ChallTimeId;
		}
		return 0u;
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
