using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 新手礼包直发（2026-11-14 新增，修复记录第 299 节）。
///
/// 用途有两个：
///   1) 新账号的登录邮件已经自带这套材料（MailService.WelcomeStarter），本命令给老账号补一份；
///   2) 实测入口 —— 走的是和邮件领取完全相同的发放路径
///      （MailService.TryGrantAttachmentCurrency → 货币载体换货币，其余走 IBLogic_BuyGoods.GrantRewardAsync），
///      不需要真的去建新号就能验证数字对不对。
///
/// 内容（口径见 MailService.WelcomeStarter）：
///   记忆嵌片 99999（记忆补嵌包 4-6-1-1 ×33333，1 包 = 3 嵌片）
///   智略点   99999（智略点物品 4-1-19-1 ×99999，1 件 = 1 点）
///   武格启芯 妮塔-悸响 / 晴-霆光 / 猫汐尔-莲驱 / 芙提雅-炬芯 各 3 个（5-32-1~4-1）
/// </summary>
[CommandInfo("startergift", "Give the newbie starter pack (memento / science points / 4 chips).", "Usage: /startergift  (append @uid)", new string[]
{
	"newgift",
	"starterkit"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandStarterGift : ICommands
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
		foreach (uint[] row in MailService.WelcomeStarter)
		{
			SimpleItem att = new SimpleItem
			{
				G = row[0],
				D = row[1],
				P = row[2],
				L = row[3],
				Count = row[4]
			};
			uint count = Math.Max(1u, att.Count);
			if (MailService.TryGrantAttachmentCurrency(player, sync, att, count))
			{
				continue;
			}
			List<uint> reward = new List<uint>
			{
				att.G,
				att.D,
				att.P,
				att.L,
				count
			};
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"startergift: 记忆嵌片 99999 / 智略点 99999 / 四件武格启芯各 3 已发放（uid {player.Uid}）。");
	}
}
