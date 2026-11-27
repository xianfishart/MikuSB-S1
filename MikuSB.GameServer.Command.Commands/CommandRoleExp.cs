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
/// 角色升级材料一套（战斗记录三档），默认每样 10000 个（2026-11-27 新增）。
///
/// 物品口径取自 Resources/item/templates/suplies.json（[Genre, Detail, Particular, Level]），
/// 名称取自 Resources/language/supplies.json 的 Key：
///   普通战斗记录  supplies.cardexp002  [5, 1, 1, 2]  Color 2  600 → 1200 经验
///   优秀战斗记录  supplies.cardexp003  [5, 1, 1, 3]  Color 3  3000 经验
///   精英战斗记录  supplies.cardexp004  [5, 1, 1, 4]  Color 4  8000 经验
/// 三档同一条 Genre/Detail/Particular 上只靠 Level 区分，与 cardexp001（新手战斗记录）相邻。
/// </summary>
[CommandInfo("roleexp", "Give character level-up records (3 kinds) to target.", "Usage: /roleexp [count=10000]  (append @uid)", new string[]
{
	"cardexp",
	"rolematerial",
	"expmat"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandRoleExp : ICommands
{
	private static readonly uint[][] Items = new uint[3][]
	{
		new uint[4] { 5u, 1u, 1u, 2u },
		new uint[4] { 5u, 1u, 1u, 3u },
		new uint[4] { 5u, 1u, 1u, 4u }
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
		await arg.SendMsg($"roleexp: 已发放普通 / 优秀 / 精英战斗记录 各 {count} 个（uid {player.Uid}）。");
	}
}
