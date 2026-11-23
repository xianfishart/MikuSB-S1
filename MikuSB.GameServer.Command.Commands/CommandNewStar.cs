using System;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 新星开拓（Star Master / VirCapture）经验发放（2026-11-15 新增，修复记录第 335/336 节）。
/// 网页面板「新星开拓等值经验」按钮调 <c>/newstar</c>，不带参数发放「升到 Lv.15 所需的经验」。
///
/// 与第 335 节的区别：不再直接写等级，改为按真实经验结算（同 VirCaptureLevel_SaveCapture.ApplyCaptureExp
/// 的升级循环），所以可以反复点击累加，等级会自然往上走，直到等级表上限（当前 50 级）。
///
/// 等级口径：
///   Gid=128 Sid=3  → 当前等级
///   Gid=128 Sid=2  → 当前级内经验
///   Gid=128 Sid=8  → 本期累计经验（有活动 MaxExp 时封顶，只影响这个显示值）
///   单次默认经验 = Σ(Level 1..14 的 Exp) = 138000（正好从 1 级升到 Lv.15）
/// 奖励位图 Sid=101~120 不动：等级涨上去后仍按界面正常领取。
/// </summary>
[CommandInfo("newstar", "Grant 新星开拓 (Star Master / VirCapture) EXP; no arg = exp equal to Lv.15 (138000), repeatable.", "Usage: /newstar [exp]  (append @uid; no value = 138000 exp ≈ Lv.15)", new string[]
{
	"vclevel",
	"starmaster"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandNewStar : ICommands
{
	private const uint Gid = 128u;

	/// <summary>当前等级。</summary>
	private const uint SidLevel = 3u;

	/// <summary>当前级内经验。</summary>
	private const uint SidExp = 2u;

	/// <summary>本期累计经验（有活动 MaxExp 时会被截断）。</summary>
	private const uint SidSeasonExp = 8u;

	/// <summary>当前活动 Id（0 = 活动未开）。</summary>
	private const uint SidActId = 1u;

	/// <summary>不带参数时，按「升到 15 级所需的经验」发放。</summary>
	private const uint TargetLevel = 15u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		if (GameData.VirCaptureLevelListData.Count == 0)
		{
			await arg.SendMsg("newstar: Resources/dlc/vircapture/levellist.json 缺失或为空，取不到等级表。");
			return;
		}
		uint maxLevel = GameData.VirCaptureLevelListData.Keys.Max();
		uint grant = (uint)Math.Max(0, arg.GetInt(0));
		bool useDefault = grant == 0;
		if (useDefault)
		{
			grant = SumExpTo(TargetLevel, maxLevel);
		}
		if (grant == 0)
		{
			await arg.SendMsg("newstar: 等级表里 Exp 全为 0，算不出等值经验。");
			return;
		}
		uint oldLevel = Math.Max(1u, player.Attributes.GetValue(Gid, SidLevel));
		uint oldExp = player.Attributes.GetValue(Gid, SidExp);
		uint oldSeason = player.Attributes.GetValue(Gid, SidSeasonExp);
		if (oldLevel >= maxLevel)
		{
			await arg.SendMsg($"newstar: 新星开拓已是满级 Lv{oldLevel}/{maxLevel}，经验不再累加。uid {player.Uid}。");
			return;
		}

		// 本期累计经验：有活动 MaxExp 就封顶（和服务器结算口径一致，只影响界面上的积分显示）。
		uint maxExp = 0u;
		uint actId = player.Attributes.GetValue(Gid, SidActId);
		if (actId != 0 && GameData.VirCaptureTimeData.TryGetValue(actId, out VirCaptureTimeExcel act))
		{
			maxExp = act.MaxExp;
		}
		ulong seasonRaw = (ulong)oldSeason + grant;
		uint newSeason = (uint)Math.Min(seasonRaw, (maxExp == 0u) ? uint.MaxValue : maxExp);

		// 升级循环：与 VirCaptureLevel_SaveCapture.ApplyCaptureExp 一致（级内经验够就扣掉、升一级）。
		uint newLevel = oldLevel;
		ulong pool = (ulong)oldExp + grant;
		while (newLevel < maxLevel
			&& GameData.VirCaptureLevelListData.TryGetValue(newLevel, out VirCaptureLevelListExcel cfg)
			&& cfg.Exp > 0
			&& pool >= cfg.Exp)
		{
			pool -= cfg.Exp;
			newLevel++;
		}
		if (newLevel >= maxLevel)
		{
			// 满级：级内经验固定成本级那一档（原结算逻辑也是这么收尾的）。
			pool = GameData.VirCaptureLevelListData.TryGetValue(maxLevel, out VirCaptureLevelListExcel maxCfg) ? maxCfg.Exp : pool;
		}
		uint newExp = (uint)Math.Min(pool, uint.MaxValue);

		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr levelAttr = player.Attributes.Set(Gid, SidLevel, newLevel);
		player.Attributes.SyncTo(sync, levelAttr);
		PlayerAttr expAttr = player.Attributes.Set(Gid, SidExp, newExp);
		player.Attributes.SyncTo(sync, expAttr);
		PlayerAttr seasonAttr = player.Attributes.Set(Gid, SidSeasonExp, newSeason);
		player.Attributes.SyncTo(sync, seasonAttr);
		await player.SendPacket(1004, sync);
		// 1004 之后再来一条 1010 ExtraSync，界面才会立刻重画（同 watergun / arcadelevel 的做法）。
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		// 属性改在内存里，必须显式落库，否则服务器重启会退回旧等级。
		DatabaseHelper.SaveDatabaseType(player.Data);

		string tail = ((newLevel >= maxLevel) ? "，已满级" : "");
		string src = (useDefault ? $"等值经验（升到 Lv{TargetLevel} 所需）" : "指定经验");
		await arg.SendMsg($"newstar: 发放{src} {grant}。Lv{oldLevel}(exp {oldExp}) -> Lv{newLevel}(exp {newExp})，累计经验 {oldSeason} -> {newSeason}{tail}，上限 Lv{maxLevel}。uid {player.Uid}。可重复点击累加；等级奖励仍走界面正常领取。");
	}

	/// <summary>Σ(Level 1..level-1 的 Exp) —— 正好够从 1 级升到 level 级。</summary>
	private static uint SumExpTo(uint level, uint maxLevel)
	{
		ulong sum = 0uL;
		for (uint l = 1u; l < level && l <= maxLevel; l++)
		{
			if (GameData.VirCaptureLevelListData.TryGetValue(l, out VirCaptureLevelListExcel cfg) && cfg.Exp > 0)
			{
				sum += cfg.Exp;
			}
		}
		return (uint)Math.Min(sum, uint.MaxValue);
	}
}
