using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 回填新星开拓「试炼吧」的测评记录（修复记录第 353 节）。
///
/// 结算页右边那一栏「测评记录 / 记录阵容」读的不是结算响应，而是**进入试炼吧时**从
/// 字符串属性 Gid=57 Sid=2 里快照下来的数组（VirCapture.GetTrialFormationData）。
/// 官服由服务端在结算时写；私服以前从来没写过，所以永远是 0、阵容永远空。
/// 结算里已经自动写了（VirCaptureTrialSettle.WriteRecord），这条命令用于：
///   · 不重打一次也能立刻补上记录；
///   · 调格式时反复试（改完立刻重登看面板）。
///
/// 用法：/trialrecord            用「本局最高分」属性（128/20000+试炼id）回填
///       /trialrecord 5000       指定分数回填
/// </summary>
[CommandInfo("trialrecord", "Backfill VirCapture Trial record (StrAttr 57/2: score + formation) so the settlement panel shows a record.", "Usage: /trialrecord [score]  (no value = current best score)", new string[] { "vctrialrecord" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandTrialRecord : ICommands
{
	private const uint AttrGid = 128u;

	private const uint BestScoreSidBase = 20000u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		var player = arg.Target.Player;
		uint trialId = player.Attributes.GetValue(AttrGid, 6u);
		if (trialId == 0)
		{
			await arg.SendMsg("trialrecord: 当前试炼 id（128/6）为 0，先让客户端进一次试炼吧。");
			return;
		}
		uint score = (uint)System.Math.Max(0, arg.GetInt(0));
		if (score == 0)
		{
			score = player.Attributes.GetValue(AttrGid, BestScoreSidBase + trialId);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		VirCaptureTrialSettle.WriteRecord(player, sync, trialId, score);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		MikuSB.Database.DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"trialrecord: 已写入 57/2，试炼 {trialId} 记录分数 {score}。请**退出到标题重登**后再进试炼吧查看（进入时才快照）。");
	}
}
