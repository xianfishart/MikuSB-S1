using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.Packet.Recv.Social;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 第 402 节（2026-11-17 21:30）：单独发一封只带「通行密钥」的邮件。
///
/// 需求原话：「通行密钥单独再发一封邮件，附件只有通行密钥 50 个」。
///
/// 【道具口径】通行密钥 = cash.json ID 12（Type=REPRESSSION，I18n=supplies.dlc2.token3，图标 1600011），
/// 它是**货币**，item/templates 里没有独立物品，邮件附件挂不上货币本身。
/// 官方载体是 others.json [4,1,6,4]：LuaType=cashbox、Param1=12、Param2 空 → 每件兑换 1 个，
/// Icon=1510108，客户端显示名就是「通行密钥」。
/// 领取时 MailService.TryGrantAttachmentCurrency 会拦下来直接 player.AddCurrency(12, n)，
/// 不会被 RewardManager 按 Param1 × Count 放大（第 299 节留下的 cashbox 一律 per=1 口径）。
/// 近义坑：others.json 里的 [4,1,25,3] 也挂 supplies.dlc2.token3 的语言键，
/// 但 Param1=51（BAGROGUECOIN3 背包乱斗币），**不是通行密钥**，别用。
///
/// 用法：/passkey [count=50]  (append @uid)
///   例：/passkey 50 @3
/// 效果：新建一封「系统邮件·通行密钥补给」，附件 1 格（4-1-6-4 × count），
///       落库后立刻推 1006，客户端重新打开邮箱即可看到并可领取。
/// </summary>
[CommandInfo("passkey", "Send a standalone mail whose only attachment is DLC2 pass keys.", "Usage: /passkey [count=50]  (append @uid)", new string[]
{
	"passmail",
	"tongxingmiyao"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandPassKey : ICommands
{
	/// <summary>通行密钥的货币载体：others.json [4,1,6,4]（cashbox → cash 12，1 件 = 1 个）。</summary>
	private static readonly uint[] PassKey = new uint[] { 4u, 1u, 6u, 4u };

	/// <summary>默认数量：用户要求 50 个。</summary>
	private const int DefaultCount = 50;

	private const string MailTitle = "系统邮件·通行密钥补给";

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		if (player == null)
		{
			await arg.SendMsg("passkey: target player is not online.");
			return;
		}
		if (!(arg.Target is Connection connection))
		{
			await arg.SendMsg("passkey: target is not a game connection.");
			return;
		}

		int count = arg.GetInt(0);
		if (count <= 0)
		{
			count = DefaultCount;
		}

		List<Mail> mails = MailService.Load(player);
		ulong mid = ((mails.Count == 0) ? 2uL : (mails.Max((Mail m) => m.Mid) + 1));
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		Mail mail = new Mail
		{
			Mid = mid,
			Title = MailTitle,
			Message = "通行密钥补给，请查收。\n领取后可在货币栏查看数量。",
			Sender = "系统",
			Pid = 0uL,
			Time = now,
			Expiration = MailService.ExpireFar,
			Life = 3650u,
			Stat = MailStat.New
		};
		mail.Attachments.Add(new SimpleItem
		{
			G = PassKey[0],
			D = PassKey[1],
			P = PassKey[2],
			L = PassKey[3],
			Count = (uint)count
		});
		mails.Add(mail);
		MailService.Save(player, mails);

		try
		{
			await connection.SendPacket(1006, mail, 0);
		}
		catch
		{
		}

		await arg.SendMsg($"passkey uid={player.Uid}: mail mid={mid} 通行密钥 x{count} ({PassKey[0]}-{PassKey[1]}-{PassKey[2]}-{PassKey[3]})，附件仅此一格。");
	}
}
