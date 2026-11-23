using System;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

public static class VigourSupplyService
{
	public const uint GroupId = 105u;

	public const uint NextResetSid = 0u;

	public const uint ClaimedMaskSid = 1u;

	private const int ResetHour = 4;

	public static long NextResetUnix(DateTime now)
	{
		DateTime dateTime = now.Date.AddHours(4.0);
		DateTime dateTime2 = ((now < dateTime) ? dateTime : dateTime.AddDays(1.0));
		return new DateTimeOffset(dateTime2).ToUnixTimeSeconds();
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (GameData.VigourSupplyData.Count != 0)
		{
			uint num = (uint)Math.Min(NextResetUnix(DateTime.Now), 4294967295L);
			uint value = player.Attributes.GetValue(105u, 0u);
			if (value != num)
			{
				SetAttr(player, 0u, num, sync);
			}
		}
	}

	public static bool IsClaimed(PlayerInstance player, uint id)
	{
		if (id == 0 || id > 31)
		{
			return false;
		}
		uint value = player.Attributes.GetValue(105u, 1u);
		return (value & (uint)(1 << (int)(id - 1))) != 0;
	}

	public static void MarkClaimed(PlayerInstance player, uint id, NtfSyncPlayer? sync)
	{
		if (id != 0 && id <= 31)
		{
			uint value = player.Attributes.GetValue(105u, 1u);
			SetAttr(player, 1u, value | (uint)(1 << (int)(id - 1)), sync);
		}
	}

	public static bool InWindow(VigourSupplyExcel row, DateTime now)
	{
		uint num = (uint)(now.Hour * 100 + now.Minute);
		uint startHhmm = row.StartHhmm;
		uint endHhmm = row.EndHhmm;
		if (startHhmm == 0 && endHhmm == 0)
		{
			return true;
		}
		return (startHhmm > endHhmm) ? (num >= startHhmm || num <= endHhmm) : (num >= startHhmm && num <= endHhmm);
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(105u, sid);
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
