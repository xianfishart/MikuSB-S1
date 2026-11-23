using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.Packet.Recv.Social;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 补发「储信终端档案邮件」（2026-11-14 新增，修复记录第 321 节）。
///
/// 登录时 MailService.SendMailsOnLogin 会自动补，但客户端断线重连有时走的是续连流程
/// （不重新发 ReqLogin），那种情况下不会触达这里。这条命令用于在线时手动补一次：
/// 内部就是重跑一遍 SendMailsOnLogin —— 它整体幂等（欢迎邮件按 mid=1、档案邮件按官方 mailid 判重），
/// 已有的邮件不会重复、也不会重置领取状态，只把缺的补上并推 1006。
///
/// 用法：/mailarc [@uid]
/// </summary>
[CommandInfo("mailarc", "Push archive mails (official mailbox data) to an online player.", "Usage: /mailarc  (append @uid)", new string[]
{
	"mailarchive",
	"mailpush"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandArchiveMails : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		if (player == null)
		{
			await arg.SendMsg("mailarc: target player is not online.");
			return;
		}
		if (!(arg.Target is Connection connection))
		{
			await arg.SendMsg("mailarc: target is not a game connection.");
			return;
		}
		await MailService.SendMailsOnLogin(connection);
		await arg.SendMsg($"mailarc uid={player.Uid}: archive mails ensured and pushed.");
	}
}
