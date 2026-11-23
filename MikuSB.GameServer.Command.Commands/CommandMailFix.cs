using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 2026-11-14（第 322 节）：档案邮件「客户端不显示」排查用命令。
///
/// 现象：服务端 12 封全推（NtfSyncNewMail × 12），客户端「邮件」列表只出现 3 封系统邮件，
/// 9 封档案邮件（官方 mailid）不显示；同一批数据在 23:32 那次是能显示的。
/// 怀疑点是客户端对 Time / Stat 的过滤，这里做对照实验：
///   /mailfix time   —— 把档案邮件的 Time 改成当前时间（其余不动）
///   /mailfix stat   —— 把 Stat 显式重设为 New
///   /mailfix all    —— 两者都改
/// 改完立刻整封重推 1006，客户端重新打开邮箱即可看到结果。
/// </summary>
[CommandInfo("mailfix", "Test: rewrite archive mails Time/Stat and repush.", "Usage: /mailfix [time|stat|all]", new string[]
{
	"mailfixtest"
}, new PermEnum[]
{
	PermEnum.Admin
})]
public class CommandMailFix : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		if (player == null || !(arg.Target is Connection connection))
		{
			await arg.SendMsg("mailfix: need online game connection.");
			return;
		}
		string mode = (arg.Args.Count > 0 ? arg.Args[0] : "all").ToLowerInvariant();
		bool fixTime = mode == "time" || mode == "all";
		bool fixStat = mode == "stat" || mode == "all";

		List<Mail> mails = MailService.Load(player);
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		int touched = 0;
		foreach (Mail m in mails)
		{
			if (!MailService.IsArchiveMid(m.Mid))
			{
				continue;
			}
			if (fixTime)
			{
				m.Time = now - 300u;
			}
			if (fixStat)
			{
				m.Stat = MailStat.New;
			}
			touched++;
		}
		MailService.Save(player, mails);
		foreach (Mail m in mails)
		{
			if (m.Stat != MailStat.Removed)
			{
				try
				{
					await connection.SendPacket(1006, m, 0);
				}
				catch
				{
				}
			}
		}
		await arg.SendMsg($"mailfix mode={mode}: touched={touched} total={mails.Count} repushed.");
	}
}
