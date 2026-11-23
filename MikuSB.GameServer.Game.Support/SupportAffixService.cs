using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Game.Support;

public static class SupportAffixService
{
	public static (uint AffixId, uint Tier) GenerateRandomAffix(int poolId, IEnumerable<uint>? excludedAffixIds = null)
	{
		if (!GameData.SupportAffixPoolData.TryGetValue(poolId, out SupportAffixPoolExcel value))
		{
			return (AffixId: 0u, Tier: 0u);
		}
		List<(IReadOnlyList<int>, int)> list = value.Groups.ToList();
		if (list.Count == 0)
		{
			return (AffixId: 0u, Tier: 0u);
		}
		int maxValue = list.Sum(((IReadOnlyList<int> Affixs, int Weight) x) => x.Weight);
		int num = Random.Shared.Next(maxValue);
		int num2 = 0;
		IReadOnlyList<int> readOnlyList = list[0].Item1;
		foreach (var item4 in list)
		{
			IReadOnlyList<int> item = item4.Item1;
			int item2 = item4.Item2;
			num2 += item2;
			if (num < num2)
			{
				readOnlyList = item;
				break;
			}
		}
		if (readOnlyList.Count == 0)
		{
			return (AffixId: 0u, Tier: 0u);
		}
		HashSet<uint> excluded = excludedAffixIds?.ToHashSet() ?? new HashSet<uint>();
		List<int> list2 = readOnlyList.Where((int x) => !excluded.Contains((uint)x)).ToList();
		if (list2.Count == 0)
		{
			list2 = readOnlyList.ToList();
		}
		int num3 = list2[Random.Shared.Next(list2.Count)];
		int maxValue2 = GameData.SupportAffixData.GetValueOrDefault(num3)?.TierCount ?? 5;
		uint item3 = (uint)(Random.Shared.Next(maxValue2) + 1);
		return (AffixId: (uint)num3, Tier: item3);
	}

	public static uint GenerateTier(uint affixId)
	{
		int maxValue = GameData.SupportAffixData.GetValueOrDefault((int)affixId)?.TierCount ?? 5;
		return (uint)(Random.Shared.Next(maxValue) + 1);
	}
}
