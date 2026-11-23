using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Dlc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 第 465 节：把「多诺-巅峰对决」整块玩法重置成新号状态。
/// 具体清理逻辑在 DonoDuelReset，这里只是控制台入口。
///
/// 用法：
///   /donoreset            重置目标玩家（控制台要带 @uid）
///   /donoreset -kick      重置后踢下线，客户端重连即重新拉全量属性
/// </summary>
[CommandInfo("donoreset", "Reset the whole DonoDuel activity to a fresh-account state.", "Usage: /donoreset [-kick]  (append @uid)", new string[]
{
	"donoreset"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandDonoReset : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int cleared = DonoDuelReset.Run(player, sync);
		await CallGSRouter.SendScript(arg.Target, "GM_AttrSync", "{}", sync);
		await arg.SendMsg($"donoreset: cleared {cleared} attribute(s) for uid={player.Uid} (season={player.Attributes.GetValue(DonoDuelReset.BagGid, DonoDuelReset.SeasonSid)}, cards={player.Attributes.GetValue(DonoDuelReset.BagGid, 2u)}).");
		if (arg.Attributes.Contains("kick"))
		{
			await arg.SendMsg("donoreset: disconnecting client to reload a clean state...");
			arg.Target.Stop();
		}
	}
}
