using System.Collections.Generic;
using System.Linq;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 465 节：把「多诺-巅峰对决」整块玩法重置成新号状态。
///
/// 每次复测首次流程都要手敲一串 /attr（185 组卡池 + 186 组任务 + 4 组引导 +
/// 21/22 关卡进度 + 货币），漏一个就会「重置了但还是老样子」，所以收敛成一处。
///
/// 清理范围：
///   185 组（保留 sid 1 赛季号）  卡池、编队、首通标记、库存、技能树
///   186 组                       剧情 / 竞技场任务（已接、已领、进度）
///   4/64100~64199                引导进度 + 引导迁移标记
///   21/22 组 5500~6510           多诺关卡星级与通关时间
///   1/127                        毛球币（货币 63）
///
/// 185/1 保留：那是「本期该用哪一季」的赛季号，新号登录时也会被
/// DonoDuelState.Ensure 写回，清掉反而让活动页短暂打不开。
/// </summary>
internal static class DonoDuelReset
{
	public const uint BagGid = 185u;

	public const uint SeasonSid = 1u;

	public const uint TaskGid = 186u;

	public const uint GuideGid = 4u;

	public const uint GuideSidStart = 64100u;

	public const uint GuideSidEnd = 64199u;

	public const uint LevelStarGid = 21u;

	public const uint LevelPassGid = 22u;

	public const uint DonoLevelStart = 5500u;

	public const uint DonoLevelEnd = 6510u;

	public const uint CurrencyGid = 1u;

	public const uint CurrencySid = 127u;

	/// <summary>清空多诺全部进度，并把被清掉的属性以 0 推给客户端。返回清理条数。</summary>
	public static int Run(PlayerInstance player, NtfSyncPlayer sync)
	{
		List<(uint Gid, uint Sid)> touched = (from PlayerAttr x in player.Attributes.All
			where IsDonoAttr(x)
			select (x.Gid, x.Sid)).ToList();
		player.Attributes.RemoveWhere((PlayerAttr x) => x.Gid == TaskGid);
		player.Attributes.RemoveWhere((PlayerAttr x) => x.Gid == BagGid && x.Sid != SeasonSid);
		player.Attributes.RemoveWhere((PlayerAttr x) => x.Gid == GuideGid && x.Sid >= GuideSidStart && x.Sid <= GuideSidEnd);
		player.Attributes.RemoveWhere((PlayerAttr x) => (x.Gid == LevelStarGid || x.Gid == LevelPassGid) && x.Sid >= DonoLevelStart && x.Sid <= DonoLevelEnd);
		player.Attributes.RemoveWhere((PlayerAttr x) => x.Gid == CurrencyGid && x.Sid == CurrencySid);
		foreach ((uint Gid, uint Sid) item in touched)
		{
			sync.Custom[player.ToPackedAttrKey(item.Gid, item.Sid)] = 0;
			sync.Custom[player.ToShiftedAttrKey(item.Gid, item.Sid)] = 0;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return touched.Count;
	}

	public static bool IsDonoAttr(PlayerAttr attr)
	{
		if (attr.Gid == BagGid)
		{
			// 赛季号不是玩家进度，保留（清掉会让活动入口短暂打不开）。
			return attr.Sid != SeasonSid;
		}
		if (attr.Gid == TaskGid)
		{
			return true;
		}
		if (attr.Gid == GuideGid)
		{
			return attr.Sid >= GuideSidStart && attr.Sid <= GuideSidEnd;
		}
		if (attr.Gid == LevelStarGid || attr.Gid == LevelPassGid)
		{
			return attr.Sid >= DonoLevelStart && attr.Sid <= DonoLevelEnd;
		}
		if (attr.Gid == CurrencyGid)
		{
			return attr.Sid == CurrencySid;
		}
		return false;
	}
}
