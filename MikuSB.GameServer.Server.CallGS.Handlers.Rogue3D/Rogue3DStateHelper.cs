using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal static class Rogue3DStateHelper
{
	private const uint GroupId = 124u;

	private const uint LevelPassStart = 20u;

	private const uint DailyBuffStart = 51u;

	private const uint DailyBuffEnd = 65u;

	private const int DailyBuffBitCount = 10;

	private const int DailyBuffBitsPerValue = 11;

	private const uint DailyBuffMask = 1023u;

	private const uint UnlockDiff1Sid = 21u;

	private const uint UnlockDiff2Sid = 22u;

	private const uint UnlockDiff3Sid = 23u;

	private const uint UnlockDiff4Sid = 24u;

	private static uint[]? ShuffledDailyBuffIds;

	public static void EnsureUavDefaults(PlayerInstance player, NtfSyncPlayer sync)
	{
		EnsureMinAttr(player, 3101u, 1u, sync);
		EnsureMinAttr(player, 3102u, 1u, sync);
	}

	public static NtfSyncPlayer EnsureUnlockState(PlayerInstance player)
	{
		NtfSyncPlayer ntfSyncPlayer = new NtfSyncPlayer();
		EnsureMinAttr(player, 21u, 1u, ntfSyncPlayer);
		EnsureMinAttr(player, 22u, 1u, ntfSyncPlayer);
		EnsureMinAttr(player, 23u, 1u, ntfSyncPlayer);
		EnsureMinAttr(player, 24u, 1u, ntfSyncPlayer);
		foreach (uint unlockTalentScienceSid in GetUnlockTalentScienceSids())
		{
			EnsureMinAttr(player, unlockTalentScienceSid, 1u, ntfSyncPlayer);
		}
		EnsureDailyBuffAttrs(player, ntfSyncPlayer);
		return ntfSyncPlayer;
	}

	private static IEnumerable<uint> GetUnlockTalentScienceSids()
	{
		return from x in (from x in GameData.Rogue3DTalentData.Values
				select x.UnlockCondition into x
				where x != 0
				select x).Distinct()
			orderby x
			select x;
	}

	private static void EnsureDailyBuffAttrs(PlayerInstance player, NtfSyncPlayer sync)
	{
		uint[] array = GetOrCreateDailyBuffIds().Take(45).ToArray();
		int num = 0;
		for (uint num2 = 51u; num2 <= 65; num2++)
		{
			uint num3 = 0u;
			int num4 = 0;
			while (num4 < 3 && num < array.Length)
			{
				num3 |= (array[num] & 0x3FF) << num4 * 11;
				num4++;
				num++;
			}
			SetAttr(player, num2, num3, sync);
		}
	}

	private static uint[] GetOrCreateDailyBuffIds()
	{
		if (ShuffledDailyBuffIds != null)
		{
			return ShuffledDailyBuffIds;
		}
		List<List<uint>> list = (from x in GameData.Rogue3DDailyBuffData.Values
			where x.ScoreBuffId != 0 && x.ScoreBuffId <= 1023
			group x by x.GroupId into x
			orderby x.Key
			select (from y in x
				orderby y.Id
				select y.ScoreBuffId).Distinct().ToList()).ToList();
		Random random = new Random();
		foreach (List<uint> item in list)
		{
			Shuffle(item, random);
		}
		Shuffle(list, random);
		List<uint> list2 = new List<uint>();
		int[] array = new int[list.Count];
		bool flag = true;
		while (flag)
		{
			flag = false;
			for (int num = 0; num < list.Count; num++)
			{
				List<uint> list3 = list[num];
				int num2 = array[num];
				if (num2 < list3.Count)
				{
					list2.Add(list3[num2]);
					array[num] = num2 + 1;
					flag = true;
				}
			}
		}
		ShuffledDailyBuffIds = list2.ToArray();
		return ShuffledDailyBuffIds;
	}

	private static IEnumerable<uint> GetDailyBuffIds()
	{
		return GetOrCreateDailyBuffIds();
	}

	private static void Shuffle<T>(IList<T> list, Random random)
	{
		for (int num = list.Count - 1; num > 0; num--)
		{
			int num2 = random.Next(num + 1);
			int index = num;
			int index2 = num2;
			T value = list[num2];
			T value2 = list[num];
			list[index] = value;
			list[index2] = value2;
		}
	}

	private static void EnsureMinAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync, bool overwrite = false)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 124u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
			AddSync(player, sync, sid, value);
		}
		else if ((overwrite || playerAttr.Val < value) && (!overwrite || playerAttr.Val != value))
		{
			playerAttr.Val = value;
			AddSync(player, sync, sid, value);
		}
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 124u,
				Sid = sid
			};
			player.Data.Attrs.Add(playerAttr);
		}
		if (playerAttr.Val != value)
		{
			playerAttr.Val = value;
			AddSync(player, sync, sid, value);
		}
	}

	private static void AddSync(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		sync.Custom[player.ToPackedAttrKey(124u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(124u, sid)] = value;
	}
}
