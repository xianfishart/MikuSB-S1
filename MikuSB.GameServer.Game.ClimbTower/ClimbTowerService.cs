using System;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.ClimbTower;

public static class ClimbTowerService
{
	private const uint StarBitMask = 255u;

	public static DlcClimbTowerSeasonExcel? SelectSeason(DateTime now)
	{
		DlcClimbTowerSeasonExcel dlcClimbTowerSeasonExcel = null;
		long num = long.MinValue;
		DlcClimbTowerSeasonExcel dlcClimbTowerSeasonExcel2 = null;
		foreach (DlcClimbTowerSeasonExcel value in GameData.ClimbTowerSeasonData.Values)
		{
			if (dlcClimbTowerSeasonExcel2 == null)
			{
				dlcClimbTowerSeasonExcel2 = value;
			}
			long num2 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value.OpenTimeRaw));
			long num3 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value.CloseTimeRaw));
			long num4 = DlcRuntimeOverrides.ToUnixSeconds(now);
			if (num2 > 0 && num3 > 0 && num4 >= num2 && num4 <= num3)
			{
				return value;
			}
			if (num2 > 0 && num2 <= num4 && num2 > num)
			{
				num = num2;
				dlcClimbTowerSeasonExcel = value;
			}
			if (dlcClimbTowerSeasonExcel2.ID < value.ID)
			{
				dlcClimbTowerSeasonExcel2 = value;
			}
		}
		return dlcClimbTowerSeasonExcel ?? dlcClimbTowerSeasonExcel2;
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		DlcClimbTowerSeasonExcel dlcClimbTowerSeasonExcel = SelectSeason(DateTime.Now);
		if (dlcClimbTowerSeasonExcel == null)
		{
			return;
		}
		if (player.Attributes.GetValue(174u, 1u) == dlcClimbTowerSeasonExcel.ID)
		{
			if (sync != null)
			{
				PlayerAttr playerAttr = player.Attributes.Get(174u, 1u);
				if (playerAttr != null)
				{
					player.Attributes.SyncTo(sync, playerAttr);
				}
			}
		}
		else
		{
			PlayerAttr attr = player.Attributes.Set(174u, 1u, dlcClimbTowerSeasonExcel.ID);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
	}

	public static uint TotalStars(PlayerInstance player, DlcClimbTowerSeasonExcel season)
	{
		uint num = 0u;
		foreach (uint battle in season.Battles)
		{
			if (!GameData.ClimbTowerBattleData.TryGetValue(battle, out DlcClimbTowerBattleExcel value) || !value.IsChallenge || value.Level == 0)
			{
				continue;
			}
			uint num2 = player.Attributes.GetValue(21u, value.Level) & 0xFF;
			for (int i = 0; i < 8; i++)
			{
				if ((num2 & (uint)(1 << i)) != 0)
				{
					num++;
				}
			}
		}
		return num;
	}

	public static bool IsAwarded(PlayerInstance player, uint awardId)
	{
		return player.Attributes.GetValue(174u, AwardSid(awardId)) != 0;
	}

	public static void MarkAwarded(PlayerInstance player, uint awardId, NtfSyncPlayer? sync)
	{
		PlayerAttr attr = player.Attributes.Set(174u, AwardSid(awardId), 1u);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
	}

	private static uint AwardSid(uint awardId)
	{
		return 10 + awardId - 1;
	}
}
