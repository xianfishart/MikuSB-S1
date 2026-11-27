using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 家具购买专用货币「奥普奖券」，默认 10000 个（2026-11-27 新增）。
///
/// 物品口径来自 Resources/item/templates/others.json（不在 suplies.json 里）：
///   奥普奖券  supplies.OppTicket  [4, 1, 6, 5]  Icon 1520023  Color 5
/// 它是宿舍家具商店的结算货币，和通用银不是一回事，所以单独一条命令、单独一个按钮。
/// </summary>
[CommandInfo("housemat", "Give furniture tickets (OppTicket) to target.", "Usage: /housemat [count=10000]  (append @uid)", new string[]
{
	"oppticket",
	"furniture",
	"houseticket"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandHouseMat : ICommands
{
	private static readonly uint[][] Items = new uint[1][]
	{
		new uint[4] { 4u, 1u, 6u, 5u }
	};

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int count = arg.GetInt(0);
		if (count <= 0)
		{
			count = 10000;
		}
		if (count > 999999)
		{
			count = 999999;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (uint[] item in Items)
		{
			List<uint> reward = new List<uint>
			{
				item[0],
				item[1],
				item[2],
				item[3],
				(uint)count
			};
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"housemat: 已发放奥普奖券 {count} 个（uid {player.Uid}）。");
	}
}
