using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 后勤基础特长重修（2026-11-14 新增，修复记录第 302 节）。
///
/// 用途：直接走 SupporterCard_ResetInitialAffix.Reset 这条和客户端完全相同的路径，
/// 用来实测「特长1、特长2 都能重修」以及材料扣除。
/// 用法：/logirb &lt;后勤卡Uid&gt; &lt;1|2&gt; [定向词条Id]  （末尾加 @uid）
/// </summary>
[CommandInfo("logirb", "Reroll a support card base affix (index 1 or 2) through the client path.", "Usage: /logirb <cardUid> <1|2> [fixedAffixId]  (append @uid)", new string[]
{
	"logirebuild"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandLogiRebuild : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		long cardUid = arg.GetInt(0);
		long index = arg.GetInt(1);
		long fixedId = arg.GetInt(2);
		if (cardUid <= 0 || index < 1 || index > 2)
		{
			await arg.SendMsg("Usage: /logirb <cardUid> <1|2> [fixedAffixId]  (append @uid)");
			return;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		if (arg.Target is not Connection connection || connection.Player == null)
		{
			await arg.SendMsg("logirb 需要在线玩家连接。");
			return;
		}
		string param = new JsonObject
		{
			["Id"] = cardUid,
			["Index"] = index,
			["FixedId"] = ((fixedId > 0) ? fixedId : 0)
		}.ToJsonString();
		bool ok = await SupporterCard_ResetInitialAffix.Reset(connection, param, fixedMode: fixedId > 0);
		await arg.SendMsg(ok
			? $"logirb card {cardUid} index {index} ok（材料已扣，新词条在待选位，回游戏点确认生效）(uid {connection.Player.Uid})"
			: $"logirb card {cardUid} index {index} 失败：见服务端日志 [SupportRebuild] (uid {connection.Player.Uid})");
	}
}
