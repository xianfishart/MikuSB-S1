using System;
using System.Collections.Generic;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛建筑状态（属性组 161）。
///
/// 客户端常量（运行时从 VirCaptureBuild 模块直接读出，第 498 节确认）：
///   GroupId        = 161
///   BuildDataStart = 10000
///   BuildDataCount = 30
///   BuildTaskOffset = { Level=1, Attr1Level=6 … Attr5Level=10, Worker1=11 … Worker5=15 }
///   BUILD_MAX_WORKER = 5, BUILD_MAX_ATTR = 5
/// 客户端 GetBuildOffData(id, off)：
///   key = BuildDataStart + id*BuildDataCount + off，且必须落在 [start, start+Count) 区间内，否则返回 0。
///   → GetBuildData(key) = me:GetAttribute(161, key)
/// 「已建成」= Level &gt; 0。
/// </summary>
internal static class VirCaptureBuildState
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildState");

	public const uint Gid = 161u;

	public const uint DataStart = 10000u;

	public const uint DataCount = 30u;

	public const uint OffLevel = 1u;

	public const uint OffAttr1 = 6u;

	public const uint OffWorker1 = 11u;

	public const uint MaxWorker = 5u;

	public const uint MaxAttr = 5u;

	// ---- 第 503 节：同组的「地图数据」区（客户端 VirCaptureBuild.GetMapOffData）----
	// 运行时实测：MapDataStart = 1000、MapDataLen = 100、tbMapIndex{326=0}。
	// MapDataOffset = { TotalOperate=1, DailyOperate=2, LevelOperate=3, GlobalBuff1..20=11..30 }
	// 也就是 326 岛的总经营次数 = 161/1001、当日次数 = 161/1002。
	public const uint MapDataStart = 1000u;

	public const uint MapDataLen = 100u;

	public const uint OffTotalOperate = 1u;

	public const uint OffDailyOperate = 2u;

	public const uint OffLevelOperate = 3u;

	public static uint MapKey(uint mapIndex, uint offset)
	{
		return MapDataStart + mapIndex * MapDataLen + offset;
	}

	public static uint GetTotalOperate(PlayerInstance player, uint mapIndex = 0u)
	{
		return player.GetAttr(Gid, MapKey(mapIndex, OffTotalOperate));
	}

	public static uint GetDailyOperate(PlayerInstance player, uint mapIndex = 0u)
	{
		return player.GetAttr(Gid, MapKey(mapIndex, OffDailyOperate));
	}

	/// <summary>
	/// 营业一次：TotalOperate / DailyOperate / LevelOperate 各 +1（客户端 HUD「累计经营」读的就是它）。
	/// </summary>
	public static void AddOperate(PlayerInstance player, NtfSyncPlayer? sync, out uint total, out uint daily)
	{
		uint mapIndex = 0u;
		total = GetTotalOperate(player, mapIndex) + 1u;
		daily = GetDailyOperate(player, mapIndex) + 1u;
		WriteRaw(player, MapKey(mapIndex, OffTotalOperate), total, sync);
		WriteRaw(player, MapKey(mapIndex, OffDailyOperate), daily, sync);
		uint levelOperate = player.GetAttr(Gid, MapKey(mapIndex, OffLevelOperate)) + 1u;
		WriteRaw(player, MapKey(mapIndex, OffLevelOperate), levelOperate, sync);
	}

	// ---- 第 517 节：经营效果等级（Type 19 Param[2]「卓越级经营效果」的判据）----
	// 营业界面有两条进度条，各按 operating_data 的 5 档阈值分级，经营效果等级取两侧较小值：
	//   员工侧进度 = 所有上岗员工 StaffProgressbar[员工等级] 之和（没派人就是 0 档）
	//   建筑侧进度 = 所有已建建筑 BuildingProgressbar[建筑等级] 之和
	// 326 岛阈值：员工 [90,160,240,340,340]、建筑 [300,400,700,1100,1100]（末位重复是封顶）。
	// 档位 = 跨过的「去重阈值」个数：0~4 档；阈值数组空（配置缺失）时按 4 档（不挡任务）兜底。

	/// <summary>数 progress 跨过了多少档去重阈值（阈值按升序去重）。</summary>
	private static uint PassedGrade(List<uint> thresholds, uint progress)
	{
		if (thresholds.Count == 0)
		{
			return 4u;
		}
		uint grade = 0u;
		uint last = uint.MaxValue;
		foreach (uint t in thresholds)
		{
			if (t == last)
			{
				continue;
			}
			last = t;
			if (progress >= t)
			{
				grade++;
			}
		}
		return grade;
	}

	/// <summary>
	/// 当前经营效果等级（0~4）。按「现在」的建筑/员工状态实时算：
	/// 与官服口径的差异是本服营业为整岛结算，进度条本来也是全岛汇总。
	/// </summary>
	public static uint ComputeEffectGrade(PlayerInstance player, uint mapId = 326u)
	{
		if (player == null)
		{
			return 0u;
		}
		uint staffProgress = 0u;
		uint buildProgress = 0u;
		foreach (VirCaptureBuildingRow cfg in VirCaptureBuildTable.All.Values)
		{
			uint level = GetLevel(player, cfg.Id);
			if (level == 0u)
			{
				continue;
			}
			if (cfg.BuildingProgressbar.Count > 0)
			{
				int idx = (int)Math.Min(level, (uint)cfg.BuildingProgressbar.Count);
				buildProgress += cfg.BuildingProgressbar[idx - 1];
			}
			for (uint slot = 1u; slot <= MaxWorker; slot++)
			{
				uint staffId = GetWorker(player, cfg.Id, slot);
				if (staffId == 0u || !VirCaptureStaffTable.TryGetStaff(staffId, out VirCaptureStaffRow srow) || srow.StaffProgressbar.Count == 0)
				{
					continue;
				}
				uint lv = VirCaptureStaffState.GetLevel(player, staffId);
				if (lv == 0u)
				{
					lv = 1u;
				}
				int sidx = (int)Math.Min(lv, (uint)srow.StaffProgressbar.Count);
				staffProgress += srow.StaffProgressbar[sidx - 1];
			}
		}
		VirCaptureStaffTable.GetEffectThresholds(mapId, out List<uint> staffTh, out List<uint> buildTh);
		uint staffGrade = PassedGrade(staffTh, staffProgress);
		uint buildGrade = PassedGrade(buildTh, buildProgress);
		uint grade = Math.Min(staffGrade, buildGrade);
		Log.Info($"[VirCapture] effect grade={grade} (staff {staffProgress}/{staffGrade}, build {buildProgress}/{buildGrade}) uid={player.Uid}");
		return grade;
	}

	/// <summary>按属性组内的绝对 key 写值（用于 1000 段的非建筑数据）。</summary>
	public static bool WriteRaw(PlayerInstance player, uint key, uint value, NtfSyncPlayer? sync)	{
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, key);
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

	public static uint Key(uint buildId, uint offset)
	{
		return DataStart + buildId * DataCount + offset;
	}

	public static uint GetLevel(PlayerInstance player, uint buildId)
	{
		return player.GetAttr(Gid, Key(buildId, OffLevel));
	}

	public static uint GetAttrLevel(PlayerInstance player, uint buildId, uint attrIndex)
	{
		if (attrIndex < 1u || attrIndex > MaxAttr)
		{
			return 0u;
		}
		return player.GetAttr(Gid, Key(buildId, OffAttr1 + attrIndex - 1u));
	}

	public static uint GetWorker(PlayerInstance player, uint buildId, uint slot)
	{
		if (slot < 1u || slot > MaxWorker)
		{
			return 0u;
		}
		return player.GetAttr(Gid, Key(buildId, OffWorker1 + slot - 1u));
	}

	/// <summary>找这名员工被派驻在哪栋建筑的第几个位置（1..5），0 = 没上岗。</summary>
	public static uint FindStaffSlot(PlayerInstance player, uint buildId, uint staffId)
	{
		for (uint slot = 1u; slot <= MaxWorker; slot++)
		{
			if (GetWorker(player, buildId, slot) == staffId)
			{
				return slot;
			}
		}
		return 0u;
	}

	public static uint FindEmptySlot(PlayerInstance player, uint buildId)
	{
		for (uint slot = 1u; slot <= MaxWorker; slot++)
		{
			if (GetWorker(player, buildId, slot) == 0u)
			{
				return slot;
			}
		}
		return 0u;
	}

	public static bool Write(PlayerInstance player, uint buildId, uint offset, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, Key(buildId, offset));
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

	/// <summary>建造（level 0→1）或升一级；返回新等级。</summary>
	public static uint Upgrade(PlayerInstance player, VirCaptureBuildingRow cfg, NtfSyncPlayer? sync)
	{
		uint level = GetLevel(player, cfg.Id);
		uint next = level + 1u;
		if (next > cfg.MaxLevel)
		{
			next = cfg.MaxLevel;
		}
		Write(player, cfg.Id, OffLevel, next, sync);
		Log.Info($"[VirCapture] build {cfg.Id}({cfg.NameKey}) {level} -> {next} uid={player.Uid}");
		return next;
	}

	/// <summary>把所有已建建筑的等级和属性等级读成一个快照，调试用。</summary>
	public static List<string> Snapshot(PlayerInstance player)
	{
		List<string> lines = new List<string>();
		foreach (VirCaptureBuildingRow cfg in VirCaptureBuildTable.All.Values)
		{
			uint level = GetLevel(player, cfg.Id);
			if (level == 0)
			{
				continue;
			}
			string attrs = string.Empty;
			for (uint i = 1u; i <= MaxAttr; i++)
			{
				attrs += GetAttrLevel(player, cfg.Id, i) + ",";
			}
			lines.Add($"build {cfg.Id} lv={level} attr=[{attrs}] worker=[{GetWorker(player, cfg.Id, 1)},{GetWorker(player, cfg.Id, 2)}]");
		}
		return lines;
	}
}
