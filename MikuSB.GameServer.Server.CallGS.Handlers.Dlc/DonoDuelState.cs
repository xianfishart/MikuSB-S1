using System;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

/// <summary>
/// 第 455 节：多诺-巅峰对决（DonoDuel）的「本期赛季号」属性。
///
/// 客户端 <c>DonoDuel:GetActId()</c> 直接读属性组 185 的 sid 1：
/// <code>me:GetAttribute(185, 1)</code>，再拿这个值当赛季号去
/// <c>DonoDuelConfig.tbSeasonList[值]</c> 取行（见 DonoDuel!GetActId / DonoDuelConfig!GetSeasonConfig）。
/// 取不到行时 <c>DonoDuelConfig:CheckOpenConfig()</c> 返回 (nil, "ui.TxtNotOpen")，
/// 于是版本活动页那张「多诺-巅峰对决」卡片点下去只弹「暂未开放」。
///
/// 官方服务端在本期带 Donoduel 玩法时会把赛季号写进 185/1：
/// 值就是 dlc_activities.json 里 DlcFuncConfId 的 <c>Donoduel=…</c>（第 35 期 = 1），
/// 同时也是 DonoDuelSeasonExcel 的主键（donoduel_season 表的 SeasonID）。
/// 这套服务端从来没写过这个属性，所以第 35 期一直是 0 → 赛季查不到 → 暂未开放。
///
/// 只在本期功能表里确实有 Donoduel 时才写，避免把往期/其它期的数据覆盖成 0。
/// </summary>
internal static class DonoDuelState
{
	/// <summary>多诺玩法的属性组（客户端 DonoDuel.GroupId）。</summary>
	public const uint Gid = 185u;

	/// <summary>组内 sid 1 = 本期赛季号（客户端 DonoDuel.nActId）。</summary>
	public const uint SeasonSid = 1u;

	/// <summary>功能表里的键名。</summary>
	private const string FuncKey = "Donoduel";

	/// <summary>
	/// 把「本期该用哪一季多诺」写进 185/1。期号没变就什么都不做。
	/// </summary>
	public static void Ensure(PlayerInstance? player, uint eraId, NtfSyncPlayer? sync)
	{
		if (player == null)
		{
			return;
		}
		uint seasonId = ResolveSeasonId(eraId);
		if (seasonId == 0)
		{
			// 本期没有多诺玩法：保持原样，别把玩家已有的赛季号清掉。
			return;
		}
		if (player.Attributes.GetValue(Gid, SeasonSid) == seasonId)
		{
			BackfillStoryTasks(player, sync);
			return;
		}
		PlayerAttr attr = player.Attributes.Set(Gid, SeasonSid, seasonId);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		BackfillStoryTasks(player, sync);
	}

	/// <summary>
	/// 第 456 节：补记「关卡早就通关、剧情任务进度还是 0」的老存档，
	/// 让客户端的自动提交（进而播放提交剧情）能正常触发。
	///
	/// 第 470 节追加：已经在培养界面做过潜能提升、但任务 104 还停在 0/1 的存档，
	/// 登录时补记一次（否则这批存档的剧情会一直卡在 104 之后）。
	/// </summary>
	private static void BackfillStoryTasks(PlayerInstance player, NtfSyncPlayer? sync)
	{
		NtfSyncPlayer target = sync ?? new NtfSyncPlayer();
		bool changed = MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.BackfillPassedArena(player, target) > 0;
		if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.HasUpgradedPotential(player)
			&& MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.AdvanceObjectiveMissions(
				player, MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.PotentialObjectiveId, target, 1u,
				MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.PotentialUpgradeKind) > 0)
		{
			changed = true;
		}
		// 第 492 节：收集任务里「累计嵌入技能 / 提升星级 / 提升潜能 N 次」（601~612）
		// 以前完全没有计数，整页永远是 0，右侧恒为「未完成」，收集奖励领不了。
		// 老存档按背包能算出来的下限补记一次（嵌技能 = 非空技能槽数、
		// 升星 = Σ(星级-1)、潜能 = 三围高于初始值的条数）。
		if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.BackfillCounterTasks(player, target) > 0)
		{
			changed = true;
		}
		// 第 483 节（追加）：301~309 / 613~620 这类「数卡」任务按背包重算，
		// 老存档里已经达标的进度（比如早就练出 5 张 2 星卡）登录后补齐。
		if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.RecalcGrowthTasks(player, target) > 0)
		{
			changed = true;
		}
		// 第 484 节（补）：Reward3 漏发的探索达人任务（201/202）重新打开成可领取，
		// 客户端会再领一次（这次真的发卡），并弹出「获得多诺卡」。只做一次。
		if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.ReopenUnpaidReward3Tasks(player, target) > 0)
		{
			changed = true;
		}
		// 第 488 节：挑战之路1（5513）首通任务 501 的卡只在 Reward3，Reward2 为空串，
		// 旧逻辑领取时一张卡都没发。用 185/699 单独落闸，只重开一次。
		if (MikuSB.GameServer.Server.CallGS.Handlers.Dlc.DonoDuelTaskState.ReopenArenaReward3Tasks(player, target) > 0)
		{
			changed = true;
		}
		if (changed)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
	}

	private static uint ResolveSeasonId(uint eraId)
	{
		if (eraId != 0)
		{
			return DlcEraFunc.Get(eraId, FuncKey);
		}
		return DlcEraFunc.Current(FuncKey);
	}
}
