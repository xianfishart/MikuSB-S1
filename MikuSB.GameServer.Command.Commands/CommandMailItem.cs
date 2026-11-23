using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 按「邮件附件」的发放路径发一件物品（2026-11-14 新增，修复记录第 301 节）。
///
/// 为什么需要：邮件附件（SimpleItem 的 G/D/P/L）和背包物品不是一套东西 ——
/// 像心流数据、智略点、记忆嵌片这些其实是货币，邮件里挂的是「货币载体物品」，
/// 领取时由 MailService.TryGrantAttachmentCurrency 换算。想验证某个附件能不能正确到账，
/// 光用 /give 发物品是测不到这条路径的，所以补这个命令。
///
/// 用法：/citem &lt;G&gt; &lt;D&gt; &lt;P&gt; &lt;L&gt; [count]  (append @uid)
///   例：/citem 4 1 33 1 99999   → 心流数据 ×99999
///       /citem 4 1 19 1 99999   → 智略点 ×99999
///       /citem 5 32 4 1 3       → 武格启芯·芙提雅-炬芯 ×3
/// </summary>
[CommandInfo("citem", "Grant one item through the mail-attachment path (currency carriers included).", "Usage: /citem <G> <D> <P> <L> [count]  (append @uid)", new string[]
{
	"mailitem"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandMailItem : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		long g = arg.GetInt(0);
		long d = arg.GetInt(1);
		long p = arg.GetInt(2);
		long l = arg.GetInt(3);
		long count = arg.GetInt(4);
		if (g <= 0 || count <= 0)
		{
			await arg.SendMsg("Usage: /citem <G> <D> <P> <L> [count]  (append @uid)");
			return;
		}
		// 控制台（网页 /api/console）执行时没有"发送者"，Target 为空会报「未找到玩家」。
		// 私服通常只有一个号在线，这里做一次兜底：唯一在线玩家就是目标，省得每次都写 @uid。
		Connection target = arg.Target as Connection;
		if (target == null)
		{
			List<Connection> online = (from c in SocketListener.Connections.Values.OfType<Connection>()
				where c.Player != null
				select c).ToList();
			if (online.Count == 1)
			{
				target = online[0];
			}
		}
		if (target?.Player == null)
		{
			await arg.SendMsg("未找到玩家：控制台执行时请加 @uid（例：/citem 4 1 33 1 99999 @3）。");
			return;
		}
		PlayerInstance player = target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		SimpleItem att = new SimpleItem
		{
			G = (uint)g,
			D = (uint)d,
			P = (uint)p,
			L = (uint)l,
			Count = (uint)count
		};
		bool asCash = MailService.TryGrantAttachmentCurrency(player, sync, att, (uint)count);
		if (!asCash)
		{
			List<uint> reward = new List<uint>
			{
				att.G,
				att.D,
				att.P,
				att.L,
				(uint)count
			};
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg($"citem {g}-{d}-{p}-{l} x{count} -> {(asCash ? "已换算为货币" : "已进背包")} (uid {player.Uid}).");
	}
}
