using System;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 建筑解锁条件求值（第 499 节新增）。
///
/// 服务端原来对 buildingcondition.json 完全不看，任何建筑都能直接建，
/// 主线「建造某建筑」之外的进度也就失去了约束。这里按客户端同一套条件语义求值：
///
///   Type 1 属性等级    VirCaptureBuildState.GetAttrLevel(Object, Data[0]) &gt;= Data[1]
///   Type 2 任务完成    Data[0] 号任务进度 &gt;= CheckValue（163 蓝图岛表 + 133 开拓目标表都查）
///   Type 3 岛屿等级    暂未实现（10 栋建筑都没用到），记 WARN 放行
///   Type 4 建筑等级    VirCaptureBuildState.GetLevel(Object) &gt;= Data[0]
///
/// 原则：**只在能明确判定时拒绝**。条件 ID 缺失、任务在两张表里都查不到、
/// 或类型不认识时一律放行并记一条 WARN —— 宁可少锁，也不能因为表没读全把主线锁死。
/// </summary>
internal static class VirCaptureBuildCondition
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildCond");

	/// <summary>检查一栋建筑的全部解锁条件；不满足时 reason 给客户端错误码。</summary>
	public static bool Check(PlayerInstance player, VirCaptureBuildingRow cfg, out string reason)
	{
		reason = string.Empty;
		foreach (uint condId in cfg.Condition)
		{
			if (!VirCaptureBuildConditionTable.TryGet(condId, out VirCaptureConditionRow row))
			{
				Log.Warn($"[VirCapture] build {cfg.Id} condition {condId} missing in buildingcondition.json -> pass");
				continue;
			}
			if (!Eval(player, cfg, row, out reason))
			{
				return false;
			}
		}
		return true;
	}

	private static bool Eval(PlayerInstance player, VirCaptureBuildingRow cfg, VirCaptureConditionRow row, out string reason)
	{
		reason = string.Empty;
		switch (row.Type)
		{
		case 1u:
		{
			uint buildId = row.Object;
			uint attrIndex = row.Arg(0);
			uint need = row.Arg(1);
			uint have = VirCaptureBuildState.GetAttrLevel(player, buildId, attrIndex);
			if (have < need)
			{
				reason = "error.condition_limit";
				Log.Info($"[VirCapture] build {cfg.Id} blocked by cond {row.Id}: build {buildId} attr {attrIndex} lv {have} < {need}");
				return false;
			}
			return true;
		}
		case 2u:
		{
			uint taskId = row.Arg(0);
			if (taskId == 0u)
			{
				taskId = row.Object;
			}
			if (taskId == 0u)
			{
				Log.Warn($"[VirCapture] build {cfg.Id} cond {row.Id}: no task id -> pass");
				return true;
			}
			int state = MissionDone(player, taskId);
			if (state < 0)
			{
				Log.Warn($"[VirCapture] build {cfg.Id} cond {row.Id}: task {taskId} not in any table -> pass");
				return true;
			}
			if (state == 0)
			{
				reason = "error.condition_limit";
				Log.Info($"[VirCapture] build {cfg.Id} blocked by cond {row.Id}: task {taskId} not finished");
				return false;
			}
			return true;
		}
		case 3u:
			Log.Warn($"[VirCapture] build {cfg.Id} cond {row.Id}: map level condition not implemented -> pass");
			return true;
		case 4u:
		{
			uint buildId = row.Object;
			uint need = row.Arg(0);
			uint have = VirCaptureBuildState.GetLevel(player, buildId);
			if (have < need)
			{
				reason = "error.condition_limit";
				Log.Info($"[VirCapture] build {cfg.Id} blocked by cond {row.Id}: build {buildId} lv {have} < {need}");
				return false;
			}
			return true;
		}
		default:
			Log.Warn($"[VirCapture] build {cfg.Id} cond {row.Id}: unknown type {row.Type} -> pass");
			return true;
		}
	}

	/// <summary>任务是否完成：1 = 完成，0 = 未完成，-1 = 两张表都没有这条。</summary>
	private static int MissionDone(PlayerInstance player, uint taskId)
	{
		if (VirCaptureBuildTaskTable.TryGet(taskId, out VirCaptureBuildTaskRow row))
		{
			return (VirCaptureBuildTaskState.GetProgress(player, taskId) >= row.CheckValue) ? 1 : 0;
		}
		if (GameData.VirCaptureMissionData.TryGetValue(taskId, out VirCaptureMissionExcel mission))
		{
			uint raw = player.GetAttr(VirCaptureMissionProgress.Gid, taskId);
			uint progress = (raw & 0xFFFFFFFEu) >> 1;
			return (progress >= mission.CheckValue) ? 1 : 0;
		}
		return -1;
	}

	/// <summary>调试用：把某栋建筑的条件逐条写成人话。</summary>
	public static string Describe(PlayerInstance player, VirCaptureBuildingRow cfg)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach (uint condId in cfg.Condition)
		{
			if (!VirCaptureBuildConditionTable.TryGet(condId, out VirCaptureConditionRow row))
			{
				sb.Append($"#{condId}(missing) ");
				continue;
			}
			string state = Eval(player, cfg, row, out _) ? "OK" : "LOCK";
			sb.Append($"#{condId}[t{row.Type} obj{row.Object} data({string.Join(",", row.Data)}) {state}] ");
		}
		return sb.ToString().TrimEnd();
	}
}
