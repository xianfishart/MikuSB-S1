using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Quest;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("quest", "Complete all quest levels for testing.", "Usage: /quest complete_all <1|0|on|off>", new string[] { "q" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandQuest : ICommands
{
	[CommandMethod("complete_all")]
	public async ValueTask CompleteAll(CommandArg arg)
	{
		if (await arg.CheckArgCnt(1))
		{
			bool? flag;
			switch (arg.Args[0].ToLowerInvariant())
			{
			case "1":
			case "on":
				flag = true;
				break;
			case "0":
			case "off":
				flag = false;
				break;
			default:
				flag = null;
				break;
			}
			bool? shouldComplete = flag;
			if (!shouldComplete.HasValue)
			{
				await arg.SendMsg("Usage: /quest complete_all <1|0|on|off>");
			}
			else if (await arg.CheckOnlineTarget())
			{
				PlayerInstance player = arg.Target.Player;
				QuestCompletionResult result = await player.QuestManager.SetAllLevelsForTestingAsync(shouldComplete.Value);
				await player.SendPacket(1004, result.Sync);
				string value = (shouldComplete.Value ? "completed" : "reset");
				await arg.SendMsg($"Quest levels {value}: {result.LevelCount}. No rewards were granted.");
			}
		}
	}
}
