using System;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 462 节：多诺（DLC35）新手引导 64100~64112 的初始化修复。
///
/// 现象（用户反馈）：
///   1. 第一次打「探索多诺卡」没有教程引导；
///   2. 打完一波弹出「获得奖励」，但多诺卡「暂无」、毛球币 0；
///   3. 首次打完一波后本应直接出现「完成探索」，实际没有出口。
///
/// 根因：
///   PlayerInstance.BuildLobbyBootstrapAttrs 把 GameData.GuideData 里的**所有**引导
///   都预置成 4/&lt;id&gt; = 999（999 在客户端 = 已跳过）。客户端 GuideLogic.CanOpenGuide
///   第一句就是 `if GetTask(id) == 999 then return false end`，所以 64100~64112 永远不播。
///   同时 DonoDuelConfig ConfigID 21 写的是：
///     「判断是否完成引导 ID 64107，若未完成，则掉落策划配死的奖励，
///       并视为首次进入肉鸽，需要在指定位置刷退出界面房间」
///   64107 被当成"已完成"，于是首次进入判定失效 —— 既不掉 donoduel_catch_battle 的
///   Guide_Reward，也不在 Guide_Point 房间刷退出点，奖励框自然空空如也。
///
/// 修法：
///   - 初始化（新号）不再给 64100~64199 预置 999；
///   - 老号登录时做一次迁移，把 64100~64112 清零（0 = 未开始），
///     用 4/64199 = 1 作迁移标记，之后玩家自己的进度不会被反复重置。
/// </summary>
internal static class DonoDuelGuideState
{
	private static readonly Logger Log = new Logger("DonoDuelGuideState");

	/// <summary>引导进度属性组。</summary>
	private const uint GuideGid = 4u;

	private const uint FirstSid = 64100u;

	private const uint LastSid = 64112u;

	/// <summary>迁移标记位（不在 guide 表内，仅服务端自用）。</summary>
	private const uint ResetFlagSid = 64199u;

	public static void Ensure(PlayerInstance player, NtfSyncPlayer? sync)
	{
		try
		{
			if (player.Attributes.GetValue(GuideGid, ResetFlagSid) == 1u)
			{
				return;
			}
			for (uint sid = FirstSid; sid <= LastSid; sid++)
			{
				PlayerAttr attr = player.Attributes.Set(GuideGid, sid, 0u);
				if (sync != null)
				{
					player.Attributes.SyncTo(sync, attr);
				}
			}
			PlayerAttr flag = player.Attributes.Set(GuideGid, ResetFlagSid, 1u);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, flag);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"donoduel guide 64100-64112 reset to 0 for uid={player.Uid}.");
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel guide reset failed: " + ex.Message);
		}
	}
}
