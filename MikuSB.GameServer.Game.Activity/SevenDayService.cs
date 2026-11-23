using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

public static class SevenDayService
{
	public const uint ActId = 5u;

	public const uint GroupId = 50u;

	public const uint UnlockDaySid = 53u;

	public const uint StageAwardSid = 54u;

	public const uint PointMoneyType = 5u;

	public static uint MaxDay()
	{
		uint num = 0u;
		foreach (KeyValuePair<(uint, uint), SevenDayAchievementExcel> sevenDayAchievementDatum in GameData.SevenDayAchievementData)
		{
			if (sevenDayAchievementDatum.Key.Item1 == 5 && sevenDayAchievementDatum.Key.Item2 > num)
			{
				num = sevenDayAchievementDatum.Key.Item2;
			}
		}
		return num;
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (!GameData.SevenDayInfoData.ContainsKey(5u))
		{
			return;
		}
		uint num = MaxDay();
		if (num != 0)
		{
			if (player.Attributes.GetValue(50u, 53u) < num)
			{
				SetAttr(player, 53u, num, sync);
			}
			EnsureQuestsFinished(player, sync);
		}
	}

	public static void EnsureQuestsFinished(PlayerInstance player, NtfSyncPlayer? sync)
	{
		foreach (uint item in QuestIds())
		{
			PlayerAttr orCreate = player.Attributes.GetOrCreate(7u, item);
			if ((orCreate.Val & 1) == 0 && orCreate.Val >> 1 < 1)
			{
				orCreate.Val |= 2u;
				if (sync != null)
				{
					player.Attributes.SyncTo(sync, orCreate);
				}
			}
		}
	}

	public static bool IsStageAwarded(PlayerInstance player, uint awardId)
	{
		if (awardId == 0 || awardId > 31)
		{
			return false;
		}
		uint value = player.Attributes.GetValue(50u, 54u);
		return (value & (uint)(1 << (int)(awardId - 1))) != 0;
	}

	public static void MarkStageAwarded(PlayerInstance player, uint awardId, NtfSyncPlayer? sync)
	{
		if (awardId != 0 && awardId <= 31)
		{
			uint value = player.Attributes.GetValue(50u, 54u);
			SetAttr(player, 54u, value | (uint)(1 << (int)(awardId - 1)), sync);
		}
	}

	public static uint Points(PlayerInstance player)
	{
		return PortCurrency.GetBalance(player, 5u);
	}

	public static IEnumerable<uint> QuestIds()
	{
		List<uint> list = new List<uint>();
		for (uint num = 1u; num <= MaxDay(); num++)
		{
			if (GameData.SevenDayAchievementData.TryGetValue((5u, num), out SevenDayAchievementExcel value))
			{
				list.AddRange(value.AchieveIds);
			}
		}
		return list;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(50u, sid);
		if (orCreate.Val != value)
		{
			orCreate.Val = value;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
		}
	}
}
