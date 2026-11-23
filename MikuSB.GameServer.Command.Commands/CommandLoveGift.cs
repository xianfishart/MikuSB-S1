using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Item;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 爱意礼盒直发（2026-11-15 新增，修复记录第 328 节）。
///
/// 背景：邮件「爱意礼盒·全员合集」的 16 个附件在客户端显示为灰色不可领
/// （因为该账号此前领过伊切尔那封，客户端把整个礼盒族标记为已领取）。
/// 邮件只是载体，这里改成把缺的礼盒直接塞进背包，不依赖邮件领取状态。
///
/// 道具族：4/12/16/1 ~ 4/12/31/1 = supplies.2024tana01~16，共 16 件
///   里芙 芬妮 安卡希雅 芙提雅 琴诺/莫尔索 妮塔 晴 茉莉安
///   肴 恩雅 辰星 猫汐尔 瑟瑞斯 苔丝 凯茜娅 伊切尔
///   注意 4/12/32/1（2024tana17）在语言包里是「临时17」占位，不属于任何角色，已排除。
///
/// 幂等：发放前先用 InventoryManager.GetNormalItemGDPL 查背包，已有则跳过，
/// 所以重复执行不会叠加（除非玩家把礼盒用掉了）。
/// 发放路径与邮件领取完全一致（TryGrantAttachmentCurrency → GrantRewardAsync）。
/// </summary>
[CommandInfo("lovebox", "Grant all 16 loving-gift boxes (2024tana01~16) to the backpack, skipping owned ones.", "Usage: /lovebox  (append @uid)", new string[]
{
	"lovegift",
	"alllove"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandLoveGift : ICommands
{
	private const uint Genre = 4u;

	private const uint Detail = 12u;

	private const uint ParticularBase = 16u;

	private const uint BoxCount = 16u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int granted = 0;
		int skipped = 0;
		for (uint i = 0u; i < BoxCount; i++)
		{
			uint particular = ParticularBase + i;
			bool owned = false;
			try
			{
				owned = player.InventoryManager.GetNormalItemGDPL((ItemTypeEnum)Genre, Detail, particular, 1u) != null;
			}
			catch
			{
				owned = false;
			}
			if (owned)
			{
				skipped++;
				continue;
			}
			SimpleItem att = new SimpleItem
			{
				G = Genre,
				D = Detail,
				P = particular,
				L = 1u,
				Count = 1u
			};
			if (!MailService.TryGrantAttachmentCurrency(player, sync, att, 1u))
			{
				List<uint> reward = new List<uint> { Genre, Detail, particular, 1u, 1u };
				await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
			}
			granted++;
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"lovebox: 爱意礼盒补发 {granted} 件，跳过已拥有 {skipped} 件（uid {player.Uid}）。");
	}
}
