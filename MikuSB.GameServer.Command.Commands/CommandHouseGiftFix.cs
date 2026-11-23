using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 补发「已赠送但没加好感」的礼物好感（2026-11-13 新增，修复记录第 283 节）。
/// 客户端用房间家具位判断礼物是否已赠送，旧版服务端把礼物消耗掉、位也置了，但好感没加，
/// 这些礼物在界面上显示「已赠送」且无法再送 → 用本命令按已置位槽位一次性补发（幂等）。
///
/// /lovefix          — 补发全部历史礼物好感
/// 说明：只对「找得到对应少女」的礼物补角色好感；公共区域的氛围值礼物照常补。
/// </summary>
[CommandInfo("lovefix", "Repay favor for gifts already given (idempotent).", "Usage: /lovefix  (append @uid)", new string[]
{
	"giftfix",
	"giftrepair",
	"lovecheck"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandHouseGiftFix : ICommands
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
		(int Applied, uint Love, uint House, int Girls) r = await HouseGiftRepair.RepairAsync(arg.Target, sync);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"lovefix: 补发 {r.Applied} 件已赠送礼物，角色好感 +{r.Love}（涉及 {r.Girls} 名少女），基地氛围值 +{r.House}。");
	}
}
