using System.Linq;
using System.Threading.Tasks;
using MikuSB.Configuration;
using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("debug", "Game.Command.Debug.Desc", "Game.Command.Debug.Usage", new string[] { "dbg" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandDebug : ICommands
{
	private static readonly Logger Logger = new Logger("CommandManager");

	[CommandDefault]
	public async ValueTask ToggleDebug(CommandArg arg)
	{
		string option = arg.Args.FirstOrDefault()?.ToLowerInvariant() ?? "on";
		ServerOption serverOption = ConfigManager.Config.ServerOption;
		if (1 == 0)
		{
		}
		string text = option switch
		{
			"on" => EnableDebug(serverOption), 
			"off" => DisableDebug(serverOption), 
			"simple" => EnableSimpleDebug(serverOption), 
			"detail" => EnableDetailDebug(serverOption), 
			"file" => ToggleDebugFile(serverOption), 
			_ => I18NManager.Translate("Game.Command.Debug.Usage"), 
		};
		if (1 == 0)
		{
		}
		string message = text;
		Logger.Info(message);
		await arg.SendMsg(message);
	}

	private static string EnableDebug(ServerOption serverOption)
	{
		serverOption.EnableDebug = true;
		serverOption.DebugMessage = true;
		serverOption.DebugDetailMessage = true;
		return I18NManager.Translate("Game.Command.Debug.Enabled");
	}

	private static string DisableDebug(ServerOption serverOption)
	{
		serverOption.EnableDebug = false;
		return I18NManager.Translate("Game.Command.Debug.Disabled");
	}

	private static string EnableSimpleDebug(ServerOption serverOption)
	{
		serverOption.EnableDebug = true;
		serverOption.DebugMessage = true;
		serverOption.DebugDetailMessage = false;
		return I18NManager.Translate("Game.Command.Debug.SimpleEnabled");
	}

	private static string EnableDetailDebug(ServerOption serverOption)
	{
		serverOption.EnableDebug = true;
		serverOption.DebugMessage = true;
		serverOption.DebugDetailMessage = true;
		return I18NManager.Translate("Game.Command.Debug.DetailEnabled");
	}

	private static string ToggleDebugFile(ServerOption serverOption)
	{
		serverOption.SavePersonalDebugFile = !serverOption.SavePersonalDebugFile;
		return serverOption.SavePersonalDebugFile ? I18NManager.Translate("Game.Command.Debug.FileEnabled") : I18NManager.Translate("Game.Command.Debug.FileDisabled");
	}
}
