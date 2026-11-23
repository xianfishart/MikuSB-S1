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
/// 四档「职级认证」一套（初级 / 中级 / 高级 / 特级），默认每样 10000 个（2026-11-14 新增）。
///
/// 物品口径取自 Resources/item/templates/suplies.json（[Genre, Detail, Particular, Level]），
/// 名称取自 Resources/language/supplies.json 的 Key：
///   初级职级认证  supplies.supportexp001  [5, 3, 1, 1]  Color 1
///   中级职级认证  supplies.supportexp002  [5, 3, 1, 2]  Color 2
///   高级职级认证  supplies.supportexp003  [5, 3, 1, 3]  Color 3
///   特级职级认证  supplies.supportexp004  [5, 3, 1, 4]  Color 4
/// 四档在同一条 Genre/Detail/Particular 上只靠 Level 区分，发奖时必须四个分量都给准。
/// </summary>
[CommandInfo("rankcert", "Give rank certificates (junior/mid/senior/special) to target.", "Usage: /rankcert [count=10000]  (append @uid)", new string[]
{
	"cert",
	"zhicheng",
	"rankitem"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandRankCert : ICommands
{
	private static readonly uint[][] Items = new uint[4][]
	{
		new uint[4] { 5u, 3u, 1u, 1u },
		new uint[4] { 5u, 3u, 1u, 2u },
		new uint[4] { 5u, 3u, 1u, 3u },
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
		await arg.SendMsg($"rankcert: 已发放初级 / 中级 / 高级 / 特级职级认证 各 {count} 个（uid {player.Uid}）。");
	}
}
