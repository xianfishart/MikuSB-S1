using System;
using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

public static class ActivitySignService
{
	public const uint AwardTypeDay = 1u;

	public const uint AwardTypeAmount = 2u;

	public const uint GroupId = 50u;

	public const uint IdPosStep = 10u;

	private const uint SignedDayPos = 1u;

	private const uint TodayStatusPos = 2u;

	private const uint LastSignDatePos = 3u;

	public const uint StatusSigned = 2u;

	public const uint StatusPending = 0u;

	public const string MonthlySignClass = "signin_monthly";

	private const uint DiySidOffset = 2u;

	public static uint SignedDaySid(uint activityId)
	{
		return activityId * 10 + 2 + 1;
	}

	public static uint TodayStatusSid(uint activityId)
	{
		return activityId * 10 + 2 + 2;
	}

	public static uint LastSignDateSid(uint activityId)
	{
		return activityId * 10 + 2 + 3;
	}

	public static uint TodayStamp()
	{
		DateTime now = DateTime.Now;
		return (uint)(now.Year * 10000 + now.Month * 100 + now.Day);
	}

	public static void RollDailyState(PlayerInstance player)
	{
		foreach (ActivityExcel value in GameData.ActivityData.Values)
		{
			if (string.Equals(value.Class, "signin_monthly", StringComparison.OrdinalIgnoreCase))
			{
				RollDailyState(player, value.Id, null);
			}
		}
	}

	public static void RollDailyState(PlayerInstance player, uint activityId, NtfSyncPlayer? sync)
	{
		if (!IsShortSignComplete(player, activityId) && player.Attributes.GetValue(50u, LastSignDateSid(activityId)) != TodayStamp())
		{
			uint sid = TodayStatusSid(activityId);
			if (player.Attributes.GetValue(50u, sid) != 0)
			{
				SetAttr(player, sid, 0u, sync);
			}
		}
	}

	internal static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
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

	public static uint MaxShortDay(uint activityId)
	{
		uint num = 0u;
		foreach (KeyValuePair<(uint, uint), SignShortExcel> signShortDatum in GameData.SignShortData)
		{
			if (signShortDatum.Key.Item1 == activityId && signShortDatum.Key.Item2 > num)
			{
				num = signShortDatum.Key.Item2;
			}
		}
		return num;
	}

	public static bool IsShortSignComplete(PlayerInstance player, uint activityId)
	{
		uint num = MaxShortDay(activityId);
		return num != 0 && player.Attributes.GetValue(50u, SignedDaySid(activityId)) >= num;
	}
}
