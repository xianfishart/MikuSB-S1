using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace S1Admin;

/// <summary>
/// 噬神斗场（Teamboss / SeasonBoss 玩法）当期期号播种（第 249 节）。
///
/// 客户端 SeasonBossLogic.GetCurActID() 读的是属性 (18,1)，
/// GetCurTimeCfg() 再按它去 tbTimeCfg 里找当期时间行。
/// 属性没写 → GetCurActID()=0 → GetCurTimeCfg()=nil →
/// Challenge/SeasonBoss/SeasonBoss.lua:1064 的
/// 「attempt to index a nil value (local 'TimeCfgGroup')」 →
/// dlc24main 的 OnOpen / UpdateRed 整段抛错中断，
/// 连带心意寄语、七日派对、曲径通幽等入口的点击判定全部失效，
/// 表现为「点击后显示暂未开放」。
///
/// 旧 dll 的 ApplyPinnedSeasonBossAct 会写 (18,1)=actId、(18,2)=1，
/// 当前精简工程里这套播种已经不存在，这里补回来。
/// </summary>
public static class SeasonBossActSeed
{
	private static readonly Logger Logger = new Logger("BossSeed");

	public static void Apply(PlayerInstance player)
	{
		uint era = DlcPin.Override();
		if (DlcPin.IsDefault(era))
		{
			return;
		}
		if (!GameData.DlcActivityData.TryGetValue(era, out DlcActivityExcel row))
		{
			return;
		}
		uint actId = DlcWindowPush.ResolveFunc(row.DlcFuncConfId, "Teamboss");
		if (actId == 0)
		{
			// 第 249 节补：ID=24 的 DlcFuncConfId 已按「四十一」条目的回滚要求注释掉，
			// 这里按该条目的期号考证兜底：challenge/seasonboss/seasonlist.json 的
			// ActID 1 是版本 2.4（StartTime 20241219），与 DLC24 窗口完全吻合，
			// 且 27 期=4、28 期=5 … 36 期=11 连续可对照，所以 24 期 Teamboss=1。
			actId = FallbackTeamboss(era);
			if (actId != 0)
			{
				Logger.Info($"第 {era} 期 DlcFuncConfId 里没有 Teamboss，按内置表兜底 act={actId}。");
			}
		}
		if (actId == 0)
		{
			return;
		}
		SetAttr(player, 18u, 1u, actId);
		SetAttr(player, 18u, 2u, 1u);
		Logger.Info($"第 {era} 期 噬神斗场 act={actId} 写入属性 18/1、18/2。");
	}

	/// <summary>
	/// 第 249 节补：DlcFuncConfId 被注释掉后的兜底表。
	/// 只登记已双验证过的期号，其余返回 0 交给调用方跳过。
	/// </summary>
	private static uint FallbackTeamboss(uint era)
	{
		return era switch
		{
			24u => 1u,
			_ => 0u
		};
	}

	private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint value)
	{
		// 第 418 节：必须走 PlayerAttributes.GetOrCreate。
		// 直接 player.Data.Attrs.Add 的新条目进不了 PlayerAttributes 内部的
		// (Gid,Sid)->PlayerAttr 字典，后续 GetValue/Get 读到的还是 0；
		// 而 NtfSyncPlayer 推送与登录包都从 data.Attrs 取，于是「库里有了、内存里没有」。
		player.Attributes.GetOrCreate(gid, sid).Val = value;
	}
}
