using System;
using System.Text.Json.Nodes;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.WorldBoss;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

/// <summary>
/// 「冬日接力战」（WorldBoss，属性组 179）打完后的成绩落库 —— 第 441 节。
///
/// 客户端在关卡结束时会通过 Reconnect.Send_SettleInfo 发
///   Chapter_DealLevelSettlement { sCmd = "WorldBossLogic_LevelSettlement" | "WorldBossLogic_LevelFail",
///                                  tbParam = { nID, nActID, nLevelID, nTeamID, nSeed, nDamageNum, tbLog, tbMonster } }
/// 服务端原先这两个 sCmd 落到 GenericSettlementHandler / GenericLevelFailHandler，
/// 被 Chapter_DealLevelSettlement 的「通用处理器不接管」判定跳过，最后走默认分支原样回吐 tbParam ——
/// 于是 nDamageNum 从来没写进属性组 179，界面「已有阵容最高积分」一直是空的，也永远「暂未上榜」。
///
/// 这里统一解析 tbParam 并调用 WorldBossService.ApplyResult：
///   找到进入关卡时分配的挑战块（179/块*10+6 = 活动 id、+7 = 期数、+8 = 是否已记录），
///   把伤害写进 +2 并把 +8 置 1，同时刷新 380/381/382 三类进度任务。
/// </summary>
internal static class WorldBossSettlementWriter
{
	private static readonly Logger Log = new Logger("WorldBossSettle");

	/// <summary>解析 tbParam 并把成绩写进属性组 179，返回本次伤害。</summary>
	public static uint Apply(PlayerInstance player, JsonObject? tb, NtfSyncPlayer sync)
	{
		if (player == null || tb == null)
		{
			return 0u;
		}
		uint activityId = ReadUInt(tb, "nID");
		uint actId = ReadUInt(tb, "nActID");
		uint levelId = ReadUInt(tb, "nLevelID");
		if (levelId == 0)
		{
			levelId = ReadUInt(tb, "nLevelId");
		}
		uint damage = ReadUInt(tb, "nDamageNum");
		if (damage == 0)
		{
			damage = ReadUInt(tb, "nDamage");
		}
		WorldBossActExcel act = WorldBossService.FindAct(activityId, actId) ?? WorldBossService.SelectAct(DateTime.Now);
		if (act == null)
		{
			Log.Warn($"settle: no act for activity={activityId} act={actId}");
			return 0u;
		}
		if (levelId == 0)
		{
			levelId = (act.LevelList.Count > 0) ? act.LevelList[0] : 0u;
		}
		if (levelId == 0)
		{
			return 0u;
		}
		try
		{
			WorldBossService.ApplyResult(player, act, levelId, damage, sync);
			Log.Info($"settle act={act.ActivityId}/{act.ActId} stage={levelId} damage={damage} uid={player.Uid}");
		}
		catch (Exception ex)
		{
			Log.Warn($"settle failed: {ex.GetType().Name}: {ex.Message}");
		}
		return damage;
	}

	private static uint ReadUInt(JsonObject tb, string key)
	{
		JsonNode? node = tb[key];
		if (node == null)
		{
			return 0u;
		}
		try
		{
			return (uint)Math.Max(0L, node.GetValue<long>());
		}
		catch
		{
			uint result;
			return uint.TryParse(node.ToString(), out result) ? result : 0u;
		}
	}
}
