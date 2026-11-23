using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 验证战场「部件」直发（2026-11-14 新增，修复记录第 315 节）。
///
/// 用途：测试部件精制 / 装配 / 词条时不用真去打增益试炼，一条命令把每个品质发够。
/// 部件不是物品，是属性组 124 的位打包值（见 Rogue3DUavGrant）：
///   sid = 3105 + idx（3105~3364，共 260 槽）
///   bit0..5 部件ID / bit6..17 词条ID / bit18..27 数值 / bit28..31 装配槽
///
/// 用法：/r3parts [每品质数量=20]  → 品质 1/2/3 各发 N 个，
///       N 个里优先把该品质的 12 种（Type 1~4 × SubType 1~3）全部铺一遍，多出来的轮转。
///       槽位不够时按剩余槽位截断并在回包消息里说明。
/// </summary>
[CommandInfo("r3parts", "Give Rogue3D UAV parts: N per quality (default 20).", "Usage: /r3parts [perQuality=20]  (append @uid)", new string[]
{
	"uavparts",
	"parts"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandRogue3Parts : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		int perQuality = (int)arg.GetInt(0);
		if (perQuality <= 0)
		{
			perQuality = 20;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		if (player == null)
		{
			await arg.SendMsg("r3parts: target player is not online.");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		Rogue3DUavGrant.GrantResult result = Rogue3DUavGrant.GrantEachQuality(player, perQuality, sync);
		if (result.Total == 0)
		{
			await arg.SendMsg($"r3parts uid={player.Uid}: nothing granted (config missing or slots full).");
			return;
		}
		await player.SendPacket(1004, sync);
		if (arg.Target is Connection connection)
		{
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);

		string detail = string.Join(", ", result.ByQuality.Select(((int Quality, int Count) x) => $"q{x.Quality}={x.Count}"));
		await arg.SendMsg($"r3parts uid={player.Uid}: {detail} total={result.Total} per={perQuality}" +
			(result.SlotsFull ? " [slots full, truncated]" : "") +
			$", slots used={result.SlotsUsed}/{Rogue3DUavGrant.PartEnd - Rogue3DUavGrant.PartBegin}.");
	}
}
