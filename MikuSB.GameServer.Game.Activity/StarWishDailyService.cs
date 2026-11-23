using System;
using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 星祈心愿「希冀点缀」的每日重置 —— 第 439 节。
///
/// 官方口径（starwish.TxtTutorial_GuideDes05）：每天任选 3 个任务完成，即可领取全部奖励。
/// 所以每天 04:00 换日时要把这 7 条重复任务的进度和「已领取（已提交）」位一起清掉，
/// 同时清掉「今日已发奖」标记，第二天才能重新做、重新领。
///
/// 属性约定（与客户端 StarWishLogic 一致）：
///   · 组 7 / 任务 ID：bit0 = 已提交（客户端显示「已领取」），bit1 起 = 进度（ActivityQuestService）。
///   · 组 180 / 6：bit0 = 今天已经领过全部奖励。
///   · 组 180 / 10：上次执行换日的「活动日」yyyymmdd，用于判断是否跨 4 点。
/// </summary>
public static class StarWishDailyService
{
	/// <summary>星祈心愿属性组（客户端 StarWishLogic.GID）。</summary>
	public const uint StarWishGid = 180u;

	/// <summary>重复任务「今日已领全部奖励」标记（客户端 nRepeatQuestExtraAward 之外的那个位，见 GetRepeatQuestAward）。</summary>
	public const uint RepeatGotSid = 6u;

	/// <summary>每档奖励的「已领取」位（客户端 GetRepeatQuestExtraAwardState(1..3) 读的就是这个）。</summary>
	public const uint ExtraAwardSid = 4u;

	/// <summary>上次换日的活动日（yyyymmdd，04:00 起算）。</summary>
	public const uint DailyDaySid = 10u;

	/// <summary>换日小时：04:00。</summary>
	private const int ResetHour = 4;

	/// <summary>取「活动日」——凌晨 0~3 点仍算前一天。</summary>
	public static int ActivityDay(DateTime now)
	{
		DateTime d = now.AddHours(-ResetHour).Date;
		return d.Year * 10000 + d.Month * 100 + d.Day;
	}

	/// <summary>星祈心愿的 7 条重复任务 ID（配置取不到时用默认表兜底）。</summary>
	public static List<uint> RepeatQuestIds()
	{
		foreach (StarWishMainExcel act in GameData.StarWishMainData.Values)
		{
			if (act.RepeatQuests != null && act.RepeatQuests.Count > 0)
			{
				return act.RepeatQuests;
			}
		}
		return new List<uint> { 23001u, 23002u, 23003u, 23004u, 23005u, 23006u, 23007u };
	}

	/// <summary>
	/// 跨过 04:00 就清零。返回被改动过的同步包（无变化返回 null）。
	/// 传入 sync 时会把改动合并进这个包（调用方自己负责发送）。
	/// </summary>
	public static NtfSyncPlayer? EnsureReset(PlayerInstance player, bool save = true, NtfSyncPlayer? sync = null)
	{
		if (player == null)
		{
			return null;
		}
		uint today = (uint)ActivityDay(DateTime.Now);
		PlayerAttr? mark = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == StarWishGid && x.Sid == DailyDaySid);
		if (mark == null)
		{
			// 首次记录：只打上今天的标记，不做清零（避免新号/老档被误清）。
			mark = player.Attributes.GetOrCreate(StarWishGid, DailyDaySid);
			mark.Val = today;
			if (save)
			{
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
			NtfSyncPlayer firstSync = sync ?? new NtfSyncPlayer();
			player.Attributes.SyncTo(firstSync, mark);
			return firstSync;
		}
		if (mark.Val == today)
		{
			// 同一天：顺手修一下「整包已领、宝箱位却没铺满」的老存档
			// （第 439 节按 bit(nPos-1) 写，界面只勾了两个箱子）。
			return HealBoxes(player, save, sync);
		}

		NtfSyncPlayer bag = sync ?? new NtfSyncPlayer();
		mark.Val = today;
		player.Attributes.SyncTo(bag, mark);

		// 1) 重复任务：进度 + 已提交位整值清零。
		foreach (uint questId in RepeatQuestIds())
		{
			PlayerAttr? attr = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == ActivityQuestService.QuestGid && x.Sid == questId);
			if (attr == null)
			{
				continue;
			}
			attr.Val = 0u;
			player.Attributes.SyncTo(bag, attr);
		}

		// 2) 「今日已领全部奖励」标记 + 每档奖励的已领位一起清零。
		foreach (uint sid in new uint[2] { RepeatGotSid, ExtraAwardSid })
		{
			PlayerAttr? flagAttr = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == StarWishGid && x.Sid == sid);
			if (flagAttr != null)
			{
				flagAttr.Val = 0u;
				player.Attributes.SyncTo(bag, flagAttr);
			}
		}

		if (save)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		Logger.GetByClassName().Info($"星祈心愿换日重置：uid={player.Uid} 活动日={today} 任务 {RepeatQuestIds().Count} 条");
		return bag;
	}

	/// <summary>
	/// 自愈：今天已经整包领过（180/6 bit0 = 1）时，180/4 的宝箱位必须全部打勾。
	/// 客户端 GetRepeatQuestExtraAwardState(i) = GetBits(180/4, i)，位序是从 0 数的，
	/// 所以三档宝箱要铺 bit0 ~ bit3（两种位序下都成立）。
	/// 只在「已整包领过」时补位，不会把没领的箱子错误标成已领。
	/// </summary>
	public static NtfSyncPlayer? HealBoxes(PlayerInstance player, bool save = true, NtfSyncPlayer? sync = null)
	{
		if (player == null)
		{
			return null;
		}
		PlayerAttr? flag = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == StarWishGid && x.Sid == RepeatGotSid);
		if (flag == null || (flag.Val & 1u) == 0)
		{
			return null;
		}
		int count = BoxCount();
		if (count <= 0 || count >= 30)
		{
			return null;
		}
		uint need = (1u << (count + 1)) - 1u;
		uint boxes = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == StarWishGid && x.Sid == ExtraAwardSid)?.Val ?? 0u;
		if ((boxes & need) == need)
		{
			return null;
		}
		NtfSyncPlayer bag = sync ?? new NtfSyncPlayer();
		SetAttr(player, ExtraAwardSid, boxes | need, bag);
		if (save)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		Logger.GetByClassName().Info($"星祈心愿宝箱位修正：uid={player.Uid} {boxes} -> {boxes | need}");
		return bag;
	}

	/// <summary>奖励档数（main.json RepeatQuestAward 的行数，正常是 3）。</summary>
	public static int BoxCount()
	{
		foreach (StarWishMainExcel act in GameData.StarWishMainData.Values)
		{
			if (act.RepeatQuestAwards != null && act.RepeatQuestAwards.Count > 0)
			{
				return act.RepeatQuestAwards.Count;
			}
		}
		return 3;
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr? attr = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == StarWishGid && x.Sid == sid);
		if (attr == null)
		{
			attr = player.Attributes.GetOrCreate(StarWishGid, sid);
		}
		attr.Val = value;
		player.Attributes.SyncTo(sync, attr);
	}
}
