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
/// 「好伙伴券」发放（2026-11-15 新增，修复记录第 337 节）。默认 10000 个。
///
/// 物品口径取自 Resources/item/templates/suplies.json：
///   好伙伴券  supplies.vircapture.token2  [5, 25, 2, 6]
///   Icon 1520057、Color 5、CanStack 1（可堆叠）
/// 用途：新星开拓「快捷劝降」消耗，快速随机劝降伙伴（见 supplies.json 的 vircapture.token2_des）。
/// </summary>
[CommandInfo("goodpartner", "Give 好伙伴券 (VirCapture quick-persuade token) to target.", "Usage: /goodpartner [count=10000]  (append @uid)", new string[]
{
	"partner",
	"token2",
	"hhrquan"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandGoodPartner : ICommands
{
	/// <summary>[G, D, P, L] = 5-25-2-6。</summary>
	private static readonly uint[] ItemGdpl = new uint[4] { 5u, 25u, 2u, 6u };

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
		List<uint> reward = new List<uint>
		{
			ItemGdpl[0],
			ItemGdpl[1],
			ItemGdpl[2],
			ItemGdpl[3],
			(uint)count
		};
		await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"goodpartner: 已发放好伙伴券 {count} 个（5-25-2-6，uid {player.Uid}）。");
	}
}
