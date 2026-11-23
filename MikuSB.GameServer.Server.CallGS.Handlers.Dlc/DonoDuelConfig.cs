using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

internal static class DonoDuelConfig
{
	public const uint AttrAttackConfigId = 1u;

	public const uint QualityCostConfigId = 2u;

	public const uint QualityUnlockConfigId = 3u;

	public const uint SkillLearningCostConfigId = 4u;

	public const uint SkillReshapingCostConfigId = 5u;

	public const uint RecycleStarConfigId = 13u;

	public const uint RecycleQualityConfigId = 14u;

	public const uint RecycleAttrConfigId = 15u;

	public const uint QualityMaxConfigId = 16u;

	public const uint InitBagConfigId = 19u;

	public const uint AttrDefenceConfigId = 23u;

	public const uint AttrHealthConfigId = 24u;

	private static DonoDuelConfigExcel? Get(uint configId)
	{
		return GameData.DonoDuelConfigData.GetValueOrDefault(configId);
	}

	public static uint QualityMax()
	{
		uint num = Get(16u)?.Scalar() ?? 0;
		return (num == 0) ? 6u : num;
	}

	public static uint InitBag()
	{
		return Get(19u)?.Scalar() ?? 150;
	}

	public static uint QualityCost(uint targetQuality)
	{
		return Get(2u)?.ValueAt(targetQuality) ?? 0;
	}

	public static IReadOnlyList<IReadOnlyList<uint>> QualityUnlockConditions(uint quality)
	{
		return Get(3u)?.RowsAt(quality) ?? Array.Empty<IReadOnlyList<uint>>();
	}

	public static uint SkillLearningCost()
	{
		return Get(4u)?.Flat().FirstOrDefault() ?? 0;
	}

	public static uint SkillReshapingCost(uint lockedSlots)
	{
		IReadOnlyList<uint> readOnlyList = Get(5u)?.Flat() ?? Array.Empty<uint>();
		if (readOnlyList.Count == 0)
		{
			return 0u;
		}
		uint result;
		if (lockedSlots >= readOnlyList.Count)
		{
			result = readOnlyList[readOnlyList.Count - 1];
		}
		else
		{
			result = readOnlyList[(int)lockedSlots];
		}
		return result;
	}

	public static (uint Cost, uint GainMin, uint GainMax) AttrGain(uint position, uint quality)
	{
		if (1 == 0)
		{
		}
		uint num = position switch
		{
			1u => 1u, 
			2u => 23u, 
			3u => 24u, 
			_ => 0u, 
		};
		if (1 == 0)
		{
		}
		uint num2 = num;
		object obj;
		if (num2 != 0)
		{
			obj = Get(num2)?.QualityRow(quality) ?? Array.Empty<uint>();
		}
		else
		{
			IReadOnlyList<uint> readOnlyList = Array.Empty<uint>();
			obj = readOnlyList;
		}
		IReadOnlyList<uint> readOnlyList2 = (IReadOnlyList<uint>)obj;
		return (readOnlyList2.Count >= 4) ? (Cost: readOnlyList2[1], GainMin: readOnlyList2[2], GainMax: readOnlyList2[3]) : (Cost: 0u, GainMin: 0u, GainMax: 0u);
	}

	public static uint RecycleStar(uint star)
	{
		return Get(13u)?.ValueAt(star) ?? 0;
	}

	public static uint RecycleQuality(uint quality)
	{
		return Get(14u)?.ValueAt(quality) ?? 0;
	}

	public static (uint Per, uint Reward) RecycleAttr()
	{
		IReadOnlyList<uint> readOnlyList = Get(15u)?.Flat() ?? Array.Empty<uint>();
		return (readOnlyList.Count >= 2) ? (Per: readOnlyList[0], Reward: readOnlyList[1]) : (Per: 1000u, Reward: 3u);
	}
}
