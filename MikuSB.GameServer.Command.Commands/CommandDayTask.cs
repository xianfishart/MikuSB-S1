using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 2026-11-17（第 415 节）：日常任务排查/自测命令。
///
/// /daytask &lt;type&gt; &lt;amount&gt; — 按「事件类型」给在线玩家的任务记一次进度，
/// 与真实行为走的是同一条 TaskEventService.AddByType 通路，不直接改属性。
/// 常用 type：14 = 通关关卡次数、4 = 消耗感知、8 = 商店购买、
///            21 = 队员升级、38 = 武器升级、44 = 后勤升级、68 = 队员故事、120 = 宿舍随机事件。
/// 用途：不想真打一局/真买一次东西，也能验证「日常进度到底有没有写进去、客户端有没有刷新」。
/// </summary>
[CommandInfo("daytask", "Test: advance task progress by event type (Group 1/3/4).", "Usage: /daytask <type> <amount>  (append @uid)", new string[]
{
	"dtask"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandDayTask : ICommands
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
			await arg.SendMsg("daytask: need an online player.");
			return;
		}
		if (arg.Args.Count < 2)
		{
			await arg.SendMsg("Usage: /daytask <type> <amount>");
			return;
		}
		if (!int.TryParse(arg.Args[0], out int type) || !int.TryParse(arg.Args[1], out int amount))
		{
			await arg.SendMsg("Usage: /daytask <type> <amount>");
			return;
		}
		TaskEventService.AddByType(player, type, amount);
		TaskEventService.Flush(player);
		await arg.SendMsg($"daytask: type {type} +{amount} applied.");
	}
}
