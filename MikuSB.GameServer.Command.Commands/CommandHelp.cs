using System.Linq;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("help", "Game.Command.Help.Desc", "Game.Command.Help.Usage", new string[] { "h" }, new PermEnum[]
{
	PermEnum.Support,
	PermEnum.Trial
})]
public class CommandHelp : ICommands
{
	[CommandDefault]
	public static async ValueTask Help(CommandArg arg)
	{
		if (arg.Args.Count == 1)
		{
			string cmd = arg.Args[0];
			if (CommandManager.CommandInfo == null || !CommandManager.CommandInfo.TryGetValue(cmd, out CommandInfoAttribute command))
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.CommandNotFound"));
				return;
			}
			string msg = $"/{command.Name} - {I18NManager.Translate(command.Description)}\n{I18NManager.Translate(command.Usage)}";
			if (command.Alias.Length != 0)
			{
				msg = msg + "\n" + I18NManager.Translate("Game.Command.Help.CommandAlias") + " " + command.Alias.ToList().ToArrayString();
			}
			if (command.Perm != null)
			{
				msg = msg + "\n" + I18NManager.Translate("Game.Command.Help.CommandPermission") + " " + string.Join(", ", command.Perm.Select((PermEnum perm) => perm.ToString()));
			}
			await arg.SendMsg(msg + "\n");
			return;
		}
		await arg.SendMsg(I18NManager.Translate("Game.Command.Help.Commands"));
		if (CommandManager.CommandInfo == null)
		{
			return;
		}
		foreach (CommandInfoAttribute command2 in CommandManager.CommandInfo.Values)
		{
			string msg2 = $"/{command2.Name} - {I18NManager.Translate(command2.Description)}\n{I18NManager.Translate(command2.Usage)}";
			if (command2.Alias.Length != 0)
			{
				msg2 = msg2 + "\n" + I18NManager.Translate("Game.Command.Help.CommandAlias") + " " + command2.Alias.ToList().ToArrayString();
			}
			if (command2.Perm != null)
			{
				msg2 = msg2 + "\n" + I18NManager.Translate("Game.Command.Help.CommandPermission") + " " + string.Join(", ", command2.Perm.Select((PermEnum perm) => perm.ToString()));
			}
			await arg.SendMsg(msg2 + "\n");
		}
	}
}
