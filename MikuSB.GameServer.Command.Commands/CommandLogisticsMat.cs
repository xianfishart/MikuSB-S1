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
/// 后勤刷词条材料一套（重修额度 / 人事额度 / 特定重修额度 / 重修申请 / 特级职级认证），
/// 默认每样 1000 个（2026-11-13 新增，修复记录第 275 节）。
///
/// 物品口径取自 Resources/item/templates/suplies.json（[Genre, Detail, Particular, Level]）：
///   重修额度      supplies.supportbreak003  [5, 6, 2, 1]
///   人事额度      supplies.supporttoken001  [5, 7, 1, 1]
///   特定重修额度  supplies.supportbreak004  [5, 6, 3, 1]
///   重修申请      supplies.supportbreak002  [5, 6, 1, 2]
///   特级职级认证  supplies.supportexp004    [5, 3, 1, 4]
/// </summary>
[CommandInfo("logisticsmat", "Give logistic word-refresh materials (5 kinds) to target.", "Usage: /logisticsmat [count=1000]  (append @uid)", new string[]
{
	"supportmat",
	"refreshmat",
	"wordmat",
	"logistics"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandLogisticsMat : ICommands
{
	private static readonly uint[][] Items = new uint[5][]
	{
		new uint[4] { 5u, 6u, 2u, 1u },
		new uint[4] { 5u, 7u, 1u, 1u },
		new uint[4] { 5u, 6u, 3u, 1u },
		new uint[4] { 5u, 6u, 1u, 2u },
		new uint[4] { 5u, 3u, 1u, 4u }
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
			count = 1000;
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
		await arg.SendMsg($"logisticsmat: 已发放重修额度 / 人事额度 / 特定重修额度 / 重修申请 / 特级职级认证 各 {count} 个（uid {player.Uid}）。");
	}
}
