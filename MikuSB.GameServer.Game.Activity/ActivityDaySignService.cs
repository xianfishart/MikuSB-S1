using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using S1Admin;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 每日签到（sClass = signin_monthly）的真实发奖与「登录自动签到」（第 432 节）。
///
/// 背景：客户端 <c>Sign.Req_SignDay</c> 会调 <c>Activity.Quest_GetAward{nId, nType=1}</c>，
/// 服务端 Activity_GetAward 的 nType==1 分支原本只回一条空的 <c>DaySign</c> 脚本，
/// 既不记录已签、也不发奖，所以界面上永远停在「未签到」。
///
/// 这里按客户端读的属性槽实现：
///   · 已签天数 50/(id*10+3)；
///   · 今日状态 50/(id*10+4)，0 = 未签、2 = 已签（客户端 &gt;1 判已签）；
///   · 最近签到日 50/(id*10+5)，格式 yyyymmdd，跨月即视为新月重置。
/// 奖励表取 <c>activity/sign/signday.json</c>（ActivityId + DayId → SignReward）。
/// </summary>
public static class ActivityDaySignService
{
	public const string MonthlySignClass = "signin_monthly";

	private const uint MaxDay = 31u;

	public static bool IsMonthlySign(ActivityExcel conf)
	{
		return conf != null && string.Equals(conf.Class, MonthlySignClass, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>下一次该签第几天：跨月归 1，超过 31 天也从 1 开始。</summary>
	public static uint NextDay(PlayerInstance player, uint activityId)
	{
		uint signed = player.Attributes.GetValue(50u, ActivitySignService.SignedDaySid(activityId));
		uint last = player.Attributes.GetValue(50u, ActivitySignService.LastSignDateSid(activityId));
		uint stamp = ActivitySignService.TodayStamp();
		if (last != 0 && last / 100 % 100 != stamp / 100 % 100)
		{
			signed = 0u;
		}
		uint day = signed + 1;
		return day > MaxDay ? 1u : day;
	}

	private static List<IReadOnlyList<uint>> RowsOf(uint activityId, uint day)
	{
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>>();
		if (!GameData.SignDayData.TryGetValue((activityId, day), out SignDayExcel excel))
		{
			return rows;
		}
		foreach (uint[] row in DlcRuntimeOverrides.ReadRewardRows(excel.SignReward))
		{
			rows.Add(row);
		}
		return rows;
	}

	/// <summary>领取当天的每日签到奖励。返回 null 表示这一天没有奖励配置。</summary>
	public static async Task<JsonArray> SignAsync(PlayerInstance player, uint activityId, NtfSyncPlayer sync)
	{
		// 今天已经签过就不再发（客户端正常会先拦住，这里防脚本绕过重复领）。
		if (SignedToday(player, activityId))
		{
			return null;
		}
		uint day = NextDay(player, activityId);
		List<IReadOnlyList<uint>> rows = RowsOf(activityId, day);
		if (rows.Count == 0)
		{
			Logger.GetByClassName().Warn($"每日签到：活动 {activityId} 第 {day} 天在 sign/signday.json 里没有奖励行，跳过。");
			return null;
		}
		JsonArray rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		ActivitySignService.SetAttr(player, ActivitySignService.SignedDaySid(activityId), day, sync);
		ActivitySignService.SetAttr(player, ActivitySignService.TodayStatusSid(activityId), ActivitySignService.StatusSigned, sync);
		ActivitySignService.SetAttr(player, ActivitySignService.LastSignDateSid(activityId), ActivitySignService.TodayStamp(), sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		Logger.GetByClassName().Info($"每日签到：uid={player.Uid} 活动 {activityId} 第 {day} 天，发放 {rewarded.Count} 组奖励。");
		return rewarded;
	}

	/// <summary>该活动今天是否已经签过。</summary>
	public static bool SignedToday(PlayerInstance player, uint activityId)
	{
		if (player.Attributes.GetValue(50u, ActivitySignService.TodayStatusSid(activityId)) < ActivitySignService.StatusSigned)
		{
			return false;
		}
		return player.Attributes.GetValue(50u, ActivitySignService.LastSignDateSid(activityId)) == ActivitySignService.TodayStamp();
	}

	/// <summary>
	/// 登录自动签到：把所有还在有效期内、今天没签的 signin_monthly 活动各签一次。
	/// 返回需要同步给客户端的属性包（没有变化时返回 null）。
	/// </summary>
	public static async Task<NtfSyncPlayer> AutoSignOnLogin(PlayerInstance player)
	{
		ActivitySignService.RollDailyState(player);
		long now = DateTimeOffset.Now.ToUnixTimeSeconds();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool any = false;
		foreach (ActivityExcel conf in GameData.ActivityData.Values)
		{
			if (!IsMonthlySign(conf))
			{
				continue;
			}
			long end = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(conf.EndTime));
			if (end > 0 && end < now)
			{
				continue;
			}
			// 第 436 节：已经退役的签到板（69 斑斓漫想 / 80 荣光盛宴）不再自动签。
			if (ActivityWindowGuard.IsRetired(conf.Id, DlcPin.Override()))
			{
				continue;
			}
			if (SignedToday(player, conf.Id))
			{
				continue;
			}
			JsonArray rewarded = await SignAsync(player, conf.Id, sync);
			if (rewarded != null)
			{
				any = true;
			}
		}
		return any ? sync : null;
	}
}
