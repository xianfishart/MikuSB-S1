using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue3D;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 逻辑研习升级（2026-11-14 新增，修复记录第 300 节）。
///
/// 用途：Rogue3D_UpgradeTech 之前不扣智略点，这里给一个能和客户端走完全相同逻辑的
/// 实测入口（Rogue3DUpgradeService.TryUpgrade），顺带方便补等级。
/// 消耗口径：server_06_sciencelevel.json 的 Cost = [25, 数量]，25 = 智略点。
/// </summary>
[CommandInfo("r3upgrade", "Upgrade a Rogue3D logic-study node (spends 智略点 like the client).", "Usage: /r3upgrade <techId>  (append @uid)", new string[]
{
	"r3up",
	"scienceup"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandRogue3Upgrade : ICommands
{
	private const uint SciencePointType = 25u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		long techId = arg.GetInt(0);
		if (techId <= 0)
		{
			await arg.SendMsg("Usage: /r3upgrade <techId>  (techId 101~149, append @uid)");
			return;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool ok = Rogue3DUpgradeService.TryUpgrade(player, (uint)techId, sync, out string error, out uint newLevel, out uint maxLevel, out uint spendType, out uint spendAmount);
		await player.SendPacket(1004, sync);
		if (arg.Target is Connection connection)
		{
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
		if (!ok)
		{
			await arg.SendMsg($"r3upgrade tech {techId} failed: {error} (uid {player.Uid}).");
			return;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		uint balance = ((spendType == 0) ? 0u : player.Attributes.GetValue(1u, spendType * 2 + 1));
		await arg.SendMsg($"r3upgrade tech {techId} -> lv{newLevel}/{maxLevel}, 智略点 -{spendAmount} = {balance} (uid {player.Uid}).");
	}
}
