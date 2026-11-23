using System;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Loader;
using MikuSB.Util;
using S1Admin;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("game", "Game.Command.Game.Desc", "Game.Command.Game.Usage", new string[] { }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandGame : ICommands
{
	private static readonly Logger Logger = new Logger("CommandManager");

	[CommandDefault]
	public async ValueTask Launch(CommandArg arg)
	{
		try
		{
			// 第 527 节补充：客户端启动前先把「因 pak 被占用而排队」的登录背景切换补上，
			// 否则 GM 切期后关掉客户端立刻 /game，会赶不上服务端写 pak。
			LoginBgReplay.ApplyPending();
			int pid = GameLaunchService.Launch(arg.Args.ToArray());
			string message = I18NManager.Translate("Game.Command.Game.Started", pid.ToString());
			Logger.Info(message);
			await arg.SendMsg(message);
		}
		catch (Exception ex)
		{
			Logger.Error("Failed to launch game.", ex);
			await arg.SendMsg(I18NManager.Translate("Game.Command.Game.Failed", ex.Message));
		}
	}
}
