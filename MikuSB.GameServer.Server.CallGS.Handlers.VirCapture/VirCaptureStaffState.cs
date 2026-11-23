using System;
using System.Collections.Generic;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛员工状态（属性组 162）。
///
/// 客户端读取链（反汇编 sd_VirCaptureStaff_*.bin 确认）：
///   GetStaffOffData(id, off) → 162 / (100 + id*20 + off)，越界返回 0
///   GetStaffLevel(id)        → off 1
///   GetStaffFlag(id)         → off 2
///   GetStaffWorkBuilding(id) → off 3
///   GetAllStaffList()        → 遍历 PalConfig.tbStaffCfg，用 GetStaffLevel 判是否拥有（&gt; 0 = 已有）
/// 所以「招到一名员工」= 把 162/(100+id*20+1) 写成它的初始等级（配置里是 1）。
/// </summary>
internal static class VirCaptureStaffState
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureStaffState");

	public const uint Gid = VirCaptureStaffTable.GroupId;

	public static uint GetLevel(PlayerInstance player, uint staffId)
	{
		return player.GetAttr(Gid, VirCaptureStaffTable.Key(staffId, VirCaptureStaffTable.OffLevel));
	}

	public static uint GetFlag(PlayerInstance player, uint staffId)
	{
		return player.GetAttr(Gid, VirCaptureStaffTable.Key(staffId, VirCaptureStaffTable.OffFlag));
	}

	public static uint GetWorkBuilding(PlayerInstance player, uint staffId)
	{
		return player.GetAttr(Gid, VirCaptureStaffTable.Key(staffId, VirCaptureStaffTable.OffWorkBuilding));
	}

	public static bool IsOwned(PlayerInstance player, uint staffId)
	{
		return staffId != 0 && GetLevel(player, staffId) > 0;
	}

	public static int OwnedCount(PlayerInstance player)
	{
		int count = 0;
		foreach (uint id in VirCaptureStaffTable.Staff.Keys)
		{
			if (GetLevel(player, id) > 0)
			{
				count++;
			}
		}
		return count;
	}

	public static bool WriteField(PlayerInstance player, uint staffId, uint offset, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, VirCaptureStaffTable.Key(staffId, offset));
		if (attr.Val == value)
		{
			return false;
		}
		attr.Val = value;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return true;
	}

	/// <summary>
	/// 招到一名员工：等级写成配置的初始等级（1），岗位清空。
	/// 已经拥有则原样返回 false（不覆盖已升级的等级）。
	/// </summary>
	public static bool Grant(PlayerInstance player, uint staffId, NtfSyncPlayer? sync)
	{
		if (staffId == 0)
		{
			return false;
		}
		if (!VirCaptureStaffTable.TryGetStaff(staffId, out VirCaptureStaffRow cfg))
		{
			Log.Warn($"[VirCapture] grant staff {staffId} failed: not in vircapture_staff.json");
			return false;
		}
		if (IsOwned(player, staffId))
		{
			return false;
		}
		uint level = (cfg.Level == 0) ? 1u : cfg.Level;
		WriteField(player, staffId, VirCaptureStaffTable.OffLevel, level, sync);
		WriteField(player, staffId, VirCaptureStaffTable.OffWorkBuilding, 0u, sync);
		Log.Info($"[VirCapture] grant staff {staffId} level={level} uid={player.Uid}");
		return true;
	}

	/// <summary>调试/清理用：把一名员工退回未拥有。</summary>
	public static bool Remove(PlayerInstance player, uint staffId, NtfSyncPlayer? sync)
	{
		if (!IsOwned(player, staffId))
		{
			return false;
		}
		WriteField(player, staffId, VirCaptureStaffTable.OffLevel, 0u, sync);
		WriteField(player, staffId, VirCaptureStaffTable.OffWorkBuilding, 0u, sync);
		return true;
	}

	public static void Save(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	/// <summary>按招募池随机出一名「还没招到」的员工；池子空了返回 0。</summary>
	public static uint PickRandomFromPool(PlayerInstance player, uint mapId)
	{
		List<uint> candidates = new List<uint>();
		foreach (VirCaptureRecruitRow row in VirCaptureStaffTable.Recruit)
		{
			if (row.MapId != mapId || row.StaffId == 0)
			{
				continue;
			}
			if (!VirCaptureStaffTable.Staff.ContainsKey(row.StaffId))
			{
				continue;
			}
			if (IsOwned(player, row.StaffId))
			{
				continue;
			}
			if (!candidates.Contains(row.StaffId))
			{
				candidates.Add(row.StaffId);
			}
		}
		if (candidates.Count == 0)
		{
			return 0u;
		}
		return candidates[Random.Shared.Next(candidates.Count)];
	}
}
