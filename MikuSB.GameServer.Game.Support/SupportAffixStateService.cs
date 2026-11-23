using System;
using MikuSB.Database.Inventory;

namespace MikuSB.GameServer.Game.Support;

public static class SupportAffixStateService
{
	public const int PairSize = 2;

	public const int MaxLogicalSlots = 5;

	public const int ActiveThirdAffixSlot = 3;

	public const int PendingMaxAffixSlot = 4;

	public const int PendingInitialAffixSlot = 5;

	public static void EnsureCapacity(GameSupportCardInfo card, int logicalSlot = 5)
	{
		int num = Math.Clamp(logicalSlot, 1, 5) * 2;
		while (card.Affixs.Count < num)
		{
			card.Affixs.Add(0u);
		}
	}

	public static (uint AffixId, uint Tier) GetAffix(GameSupportCardInfo card, int logicalSlot)
	{
		if (logicalSlot < 1 || logicalSlot > 5)
		{
			return (AffixId: 0u, Tier: 0u);
		}
		int num = (logicalSlot - 1) * 2;
		if (card.Affixs.Count <= num + 1)
		{
			return (AffixId: 0u, Tier: 0u);
		}
		return (AffixId: card.Affixs[num], Tier: card.Affixs[num + 1]);
	}

	public static bool HasAffix(GameSupportCardInfo card, int logicalSlot)
	{
		var (num, num2) = GetAffix(card, logicalSlot);
		return num != 0 && num2 != 0;
	}

	public static void SetAffix(GameSupportCardInfo card, int logicalSlot, uint affixId, uint tier)
	{
		if (logicalSlot >= 1 && logicalSlot <= 5)
		{
			EnsureCapacity(card, logicalSlot);
			int num = (logicalSlot - 1) * 2;
			card.Affixs[num] = affixId;
			card.Affixs[num + 1] = tier;
		}
	}

	public static void ClearAffix(GameSupportCardInfo card, int logicalSlot)
	{
		SetAffix(card, logicalSlot, 0u, 0u);
	}

	public static void CopyAffix(GameSupportCardInfo card, int fromSlot, int toSlot)
	{
		var (affixId, tier) = GetAffix(card, fromSlot);
		SetAffix(card, toSlot, affixId, tier);
	}

	public static uint GetVisibleInitialAffixIndex(GameSupportCardInfo card)
	{
		return HasAffix(card, 5) ? card.AffixId : 0u;
	}

	public static void NormalizePendingState(GameSupportCardInfo card)
	{
		if (!HasAffix(card, 5))
		{
			card.AffixId = 0u;
		}
	}
}
