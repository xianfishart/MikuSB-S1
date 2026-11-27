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
/// 武器培养材料一套（突破 4 种 + 强化 3 种），默认每样 10000 个（2026-11-27 新增）。
///
/// 物品口径取自 Resources/item/templates/suplies.json（[Genre, Detail, Particular, Level]），
/// 名称取自 Resources/language/supplies.json 的 Key：
///   合成颗粒      supplies.weaponbreak001  [5, 5, 1, 1]  Color 2
///   芳烃塑料      supplies.weaponbreak002  [5, 5, 1, 2]  Color 3
///   单极纤维      supplies.weaponbreak003  [5, 5, 1, 3]  Color 4
///   碳原子板      supplies.weaponbreak004  [5, 5, 1, 4]  Color 5
///   优选强化套件  supplies.weaponexp002    [5, 2, 1, 2]  Color 2
///   精致强化套件  supplies.weaponexp003    [5, 2, 1, 3]  Color 3
///   珍稀强化套件  supplies.weaponexp004    [5, 2, 1, 4]  Color 4
/// 突破与强化分属 Detail 5 / Detail 2，同 Detail 内只靠 Level 区分，发奖时四个分量必须都给准。
/// </summary>
[CommandInfo("weaponmat", "Give weapon enhancement materials (7 kinds) to target.", "Usage: /weaponmat [count=10000]  (append @uid)", new string[]
{
	"wmat",
	"weaponmaterial",
	"wqmat"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandWeaponMat : ICommands
{
	private static readonly uint[][] Items = new uint[7][]
	{
		new uint[4] { 5u, 5u, 1u, 1u },
		new uint[4] { 5u, 5u, 1u, 2u },
		new uint[4] { 5u, 5u, 1u, 3u },
		new uint[4] { 5u, 5u, 1u, 4u },
		new uint[4] { 5u, 2u, 1u, 2u },
		new uint[4] { 5u, 2u, 1u, 3u },
		new uint[4] { 5u, 2u, 1u, 4u }
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
		await arg.SendMsg($"weaponmat: 已发放合成颗粒 / 芳烃塑料 / 单极纤维 / 碳原子板 / 优选强化套件 / 精致强化套件 / 珍稀强化套件 各 {count} 个（uid {player.Uid}）。");
	}
}
