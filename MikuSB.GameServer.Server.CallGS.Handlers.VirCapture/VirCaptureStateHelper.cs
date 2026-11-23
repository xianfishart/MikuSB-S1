using System;
using System.Linq;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal static class VirCaptureStateHelper
{
	public const uint GroupId = 128u;

	public const uint MapDataStart = 10000u;

	public const uint MapDataEnd = 19000u;

	public const uint MaxMapCount = 3u;

	public const uint MaxMapDataLen = 3000u;

	public const uint MaxPatrolPoint = 500u;

	public const uint MaxOtherPoint = 2500u;

	public const uint MinMaterialId = 50000u;

	public const uint MaxMaterialId = 51500u;

	public const uint OffMapId = 1u;

	public const uint OffTurnNum = 2u;

	public const uint OffPosX = 3u;

	public const uint OffPosY = 4u;

	public const uint OffPosZ = 5u;

	public const uint OffToward = 6u;

	public const uint OffDayNight = 7u;

	public const uint OffMapLevel = 8u;

	public const uint OffPatrolStart = 51u;

	public const uint OffPatrolEnd = 1000u;

	public const uint OffOtherStart = 1001u;

	public const uint OffOtherEnd = 1500u;

	public const uint OffMaterialStart = 1501u;

	public const uint OffMaterialEnd = 3000u;

	public static uint FindOrAllocateMapSlot(PlayerInstance player, uint levelId)
	{
		uint? num = null;
		for (uint num2 = 0u; num2 < 3; num2++)
		{
			uint slotStart = 10000 + num2 * 3000;
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == slotStart + 1);
			if (playerAttr != null && playerAttr.Val == levelId)
			{
				return slotStart;
			}
			if (!num.HasValue && (playerAttr == null || playerAttr.Val == 0))
			{
				num = slotStart;
			}
		}
		return num.GetValueOrDefault();
	}

	public static void EnsureBaseMapState(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
	{
		uint num = FindOrAllocateMapSlot(player, levelId);
		if (num != 0)
		{
			EnsureUnsignedAttr(player, num + 1, levelId, sync);
			EnsureUnsignedAttr(player, num + 7, 1u, sync);
			EnsureUnsignedAttr(player, num + 8, 1u, sync);
		}
	}

	public static void SetSignedMapOffset(PlayerInstance player, uint levelId, uint offset, int value, NtfSyncPlayer sync)
	{
		uint num = FindOrAllocateMapSlot(player, levelId);
		if (num != 0)
		{
			EnsureBaseMapState(player, levelId, sync);
			SetUnsignedAttr(player, num + offset, (uint)value, sync);
		}
	}

	public static void SetPointState(PlayerInstance player, uint levelId, uint pointId, uint value, NtfSyncPlayer sync)
	{
		uint num = FindOrAllocateMapSlot(player, levelId);
		if (num == 0 || pointId == 0)
		{
			return;
		}
		EnsureBaseMapState(player, levelId, sync);
		if (pointId <= 500)
		{
			uint sid = num + 50 + pointId;
			SetUnsignedAttr(player, sid, value, sync);
		}
		else if (pointId <= 2500)
		{
			uint num2 = pointId - 500;
			uint num3 = num + (uint)Math.Floor((double)num2 / 30.0) + 1001;
			if (num3 <= num + 1500)
			{
				int num4 = (int)(num2 % 30);
				PlayerAttr orCreateAttr = GetOrCreateAttr(player, num3);
				uint num5 = ((value != 0) ? (orCreateAttr.Val | (uint)(1 << num4)) : (orCreateAttr.Val & (uint)(~(1 << num4))));
				if (num5 != orCreateAttr.Val)
				{
					orCreateAttr.Val = num5;
					SyncAttr(player, sync, num3, num5);
				}
			}
		}
		else if (pointId > 50000 && pointId <= 51500)
		{
			uint num6 = num + 1500 + (pointId - 50000);
			if (num6 < num + 3000)
			{
				SetUnsignedAttr(player, num6, value, sync);
			}
		}
	}

	/// <summary>第 505 节：读取点位状态（0 = 未处理，1 = 进行中，2 = 已完成），与 SetPointState 同一套位域。</summary>
	public static uint GetPointState(PlayerInstance player, uint levelId, uint pointId)
	{
		uint num = FindOrAllocateMapSlot(player, levelId);
		if (num == 0 || pointId == 0)
		{
			return 0;
		}
		if (pointId <= 500)
		{
			return GetUnsignedAttr(player, num + 50 + pointId);
		}
		if (pointId <= 2500)
		{
			uint num2 = pointId - 500;
			uint num3 = num + (uint)Math.Floor((double)num2 / 30.0) + 1001;
			if (num3 > num + 1500)
			{
				return 0;
			}
			int num4 = (int)(num2 % 30);
			return (GetUnsignedAttr(player, num3) >> num4) & 1u;
		}
		if (pointId > 50000 && pointId <= 51500)
		{
			uint num5 = num + 1500 + (pointId - 50000);
			if (num5 < num + 3000)
			{
				return GetUnsignedAttr(player, num5);
			}
		}
		return 0;
	}

	public static void EnsureUnsignedAttr(PlayerInstance player, uint sid, uint minValue, NtfSyncPlayer sync)
	{
		PlayerAttr orCreateAttr = GetOrCreateAttr(player, sid);
		if (orCreateAttr.Val < minValue)
		{
			orCreateAttr.Val = minValue;
			SyncAttr(player, sync, sid, orCreateAttr.Val);
		}
	}

	public static uint GetUnsignedAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid);
		return playerAttr?.Val ?? 0u;
	}

	public static void SetUnsignedAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreateAttr = GetOrCreateAttr(player, sid);
		if (orCreateAttr.Val != value)
		{
			orCreateAttr.Val = value;
			SyncAttr(player, sync, sid, value);
		}
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = 128u,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		sync.Custom[player.ToPackedAttrKey(128u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(128u, sid)] = value;
	}
}
