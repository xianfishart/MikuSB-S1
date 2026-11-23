using System;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Friend;

/// <summary>
/// 好友体力的每日领取状态。
/// 2026-11-11 新增：原版服务端没有任何好友体力存档，客户端「领取好友体力」
/// 只是一次空回包，数值不会变化。这里用一条玩家自定义属性
/// （gid=0 / sid=6901）记录最近一次领取的日期（形如 20261111），
/// 同一天重复领取不再加体力，跨天后重新可领。
/// </summary>
public static class FriendVigorState
{
	public const uint StateGid = 0u;

	/// <summary>属性 sid，避开活动/签到已用的 0~200 段。</summary>
	public const uint LastClaimSid = 6901u;

	/// <summary>每次领取给到的体力点数。</summary>
	public const uint VigorPerClaim = 20u;

	public static uint TodayKey()
	{
		DateTime now = DateTime.Now;
		return (uint)(now.Year * 10000 + now.Month * 100 + now.Day);
	}

	public static bool HasClaimedToday(PlayerInstance? player)
	{
		if (player == null)
		{
			return false;
		}
		return player.GetAttr(StateGid, LastClaimSid) == TodayKey();
	}

	public static void MarkClaimed(PlayerInstance player)
	{
		player.SetAttr(StateGid, LastClaimSid, TodayKey());
	}

	/// <summary>
	/// 当日首次领取：+20 体力（允许超出上限 20，避免满体力时点了毫无变化），
	/// 记录领取日期并落库。返回是否真的加到了体力，sync 用于刷客户端数字。
	/// </summary>
	public static bool TryClaim(PlayerInstance? player, out NtfSyncPlayer? sync)
	{
		sync = null;
		if (player == null)
		{
			return false;
		}
		if (HasClaimedToday(player))
		{
			return false;
		}
		uint max = VigorRecovery.MaxOf(player);
		ulong target = (ulong)player.Data.Vigor + VigorPerClaim;
		uint cap = max + VigorPerClaim;
		player.Data.Vigor = (uint)Math.Min(target, cap);
		MarkClaimed(player);
		DatabaseHelper.SaveDatabaseType(player.Data);
		sync = new NtfSyncPlayer();
		sync.Core[2u] = player.Data.Vigor;
		sync.Core[4u] = player.Attributes.GetValue(9900u, 1u);
		return true;
	}
}
