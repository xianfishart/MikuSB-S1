using System.Collections.Generic;
using MikuSB.Database;
using SqlSugar;

namespace MikuSB.GameServer.Game.Inventory;

/// <summary>
/// 新星开拓「开拓目标」的累计计数器（第 343 节新增）。
///
/// 为什么需要它：客户端的任务状态并不是自己算的，而是读玩家属性
/// <c>(133, MissionID)</c> 的 bit1..31 当进度、bit0 当"已领取"
/// （<c>Achievement.GetProgres / IsReceive</c> 反汇编确认）：
/// <code>
///   prog = GetBits(me:GetAttribute(133, missionId), 1, 31)
///   got  = GetBits(..., 0, 0)            -- bit0
///   IsFinished = (nCheckValue &lt;= prog)
/// </code>
/// 所以服务端必须**主动**把进度写进属性位。而这些任务的判据是"累计"类统计
/// （累计劝降 N 个、累计升阶 N 次、开拓等级达到 N……），配置表 vircapture_mission.json
/// 里没有现成的运行期数据，只能由服务端自己记。
///
/// 本表只存「事件计数器」的原始值，进度换算在 VirCaptureMissionProgress.Evaluate 里，
/// 这样改公式不用动存档。ClearedLevels 用来支撑 Type=2（"闯关吧"指定关卡通关）。
/// </summary>
[SugarTable("vircapture_mission_counter")]
public class VirCaptureMissionCounterData : BaseDatabaseDataHelper
{
	/// <summary>事件计数器：键见 VirCaptureMissionProgress 里的 Key* 常量。</summary>
	[SugarColumn(IsJson = true)]
	public Dictionary<string, long> Counters { get; set; } = new Dictionary<string, long>();

	/// <summary>已通关的等级 Id（Type=2 的任务按 Param 里的等级判断）。</summary>
	[SugarColumn(IsJson = true)]
	public List<uint> ClearedLevels { get; set; } = new List<uint>();

	/// <summary>最后一次记登录天数的日期（yyyyMMdd），防止重复计数。</summary>
	public string LastLoginDay { get; set; } = "";
}
