using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

/// <summary>
/// 2026-11-15（第 325 / 327 节）：新号登录自动发放两组纪念道具。
///   1) 储信终端纪念品：4-99-2-1 ~ 4-99-81-1（riki_souvenir_box，80 件，对应 riki.mail002~081）
///      ★ 第 348 节更正：官方图鉴表 Type13 只有 13002~13081，语言表也只有 mail002~081，
///        4-99-1-1 是未启用占位行（图标借的是能量方晶），不能再发，否则背包里会多一件
///        没有名字、点使用卡加载的假信物。
///   2) 爱意礼盒·全员合集：4-12-16-1 ~ 4-12-31-1（itembox，16 件，supplies.2024tana01~16）
/// 数据来源：客户端 PAK_Game_Settings 解包后的
///   Game/Content/Settings/item/templates/others.txt（G/D/P/L/I18n 列）
///   Game/Content/Settings/all_language/ui/supplies.txt（名字）
/// 注意：2024tana17 在表里是「临时17」占位，不属于任何角色，故 16 件封顶。
/// 幂等：按固定 Mid 判定，已存在直接跳过，不重置 Stat。
/// </summary>
internal static partial class MailService
{
	/// <summary>储信终端纪念品合集（80 件）。</summary>
	private const ulong RikiSouvenirMid = 1900000001UL;

	/// <summary>
	/// 储信终端纪念品合集正文。
	/// 第 349 节：老存档里这封邮件是「共 81 件」写死在 DB 里的，只改附件不改正文的话，
	/// 界面上的文案永远停在 81，所以正文也提成常量，登录时按需覆盖。
	/// </summary>
	private const string RikiSouvenirMessage = "把储信终端里那些纪念品一次补齐：共 80 件，来自历次邮件的收藏道具。\n"
		+ "一次性领取，之后可在道具背包里查看。";

	/// <summary>爱意礼盒·全员合集（16 件）。</summary>
	/// <remarks>
	/// 2026-11-15（第 333 节）：Mid 由 1900000003 改为 1900000004 —— 旧邮件在客户端被缓存成
	/// 「已领取」，重新把 Stat 写成 New 也刷不出可领状态，只能整封换 Mid 重发。
	/// </remarks>
	private const ulong LoveGiftBoxMid = 1900000004UL;

	/// <summary>旧版爱意礼盒邮件 Mid（第 325~332 节用过），登录时顺手清掉，避免两封并存。</summary>
	private const ulong LoveGiftBoxLegacyMid = 1900000003UL;

	/// <summary>纪念品数量：4-99-2-1 ~ 4-99-81-1（官方只有 80 件，第 348 节）。</summary>
	private const uint RikiSouvenirCount = 80u;

	/// <summary>纪念品起始 Particular：官方第 1 件不存在，从 2 开始。</summary>
	private const uint RikiSouvenirStart = 2u;

	/// <summary>爱意礼盒数量：4-12-16-1 ~ 4-12-31-1（2024tana01~16）。</summary>
	private const uint LoveGiftBoxCount = 16u;

	/// <summary>爱意礼盒起始 Detail 值（2024tana01 = 4/12/16/1）。</summary>
	private const uint LoveGiftBoxDetailBase = 16u;

	/// <summary>爱意礼盒邮件正文（补回附件时也用这段，保证和新建号一致）。</summary>
	private const string LoveGiftBoxMessage = "十六位天启者的爱意礼盒，一次全收。\n"
		+ "里芙、芬妮、安卡希雅、芙提雅、琴诺/莫尔索、妮塔、晴、茉莉安、肴、恩雅、辰星、猫汐尔、瑟瑞斯、苔丝、凯茜娅、伊切尔——"
		+ "每一份都是她们亲手准备的心意，逐一拆开看看吧。";

	private static void EnsureStarterGiftMails(PlayerInstance player, List<Mail> mails)
	{
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		bool changed = false;

		// 第 333 节：旧 Mid 的爱意礼盒邮件整体退役（客户端已缓存成已领取，刷不出来），
		// 新邮件用 LoveGiftBoxMid 重发；这里把旧的那封删掉，防止两封并存。
		if (mails.RemoveAll((Mail m) => m.Mid == LoveGiftBoxLegacyMid) > 0)
		{
			changed = true;
			MailLogger.Info($"love gift legacy mail removed uid={player.Uid} mid={LoveGiftBoxLegacyMid}");
		}

		if (!mails.Any((Mail m) => m.Mid == RikiSouvenirMid))
		{
			Mail mail = new Mail
			{
				Mid = RikiSouvenirMid,
				Title = "储信终端·纪念品合集",
				Message = RikiSouvenirMessage,
				Sender = "系统",
				Pid = 0uL,
				Time = now,
				Expiration = ExpireFar,
				Life = 3650u,
				Stat = MailStat.New
			};
			for (uint p = RikiSouvenirStart; p < RikiSouvenirStart + RikiSouvenirCount; p++)
			{
				mail.Attachments.Add(NewAttachment(new uint[5] { 4u, 99u, p, 1u, 1u }));
			}
			mails.Add(mail);
			changed = true;
			MailLogger.Info($"starter gift mails: riki souvenirs uid={player.Uid} count={mail.Attachments.Count}");
		}
		else
		{
			// 第 348 节：老存档里这封邮件带着 4-99-1-1（官方不存在的占位行）。
			// 已领取的邮件也要清 —— 附件列表会随邮件一起下发给客户端，留着就还会被画成一格「无名信物」。
			Mail existRiki = mails.FirstOrDefault((Mail m) => m.Mid == RikiSouvenirMid);
			if (existRiki != null && existRiki.Stat != MailStat.Removed)
			{
				// 第 349 节：正文字面里写着数量，老邮件存的是「共 81 件」，必须一并覆盖，
				// 否则附件已经是 80 件、界面上却还写着 81。
				if (existRiki.Message != RikiSouvenirMessage)
				{
					existRiki.Message = RikiSouvenirMessage;
					changed = true;
					MailLogger.Info($"riki souvenir mail body fixed uid={player.Uid}");
				}
				if (existRiki.Title != "储信终端·纪念品合集")
				{
					existRiki.Title = "储信终端·纪念品合集";
					changed = true;
				}
				List<SimpleItem> keep = existRiki.Attachments.Where((SimpleItem a) => !(a.G == 4u && a.D == 99u && a.P == 1u && a.L == 1u)).ToList();
				int removed = existRiki.Attachments.Count - keep.Count;
				if (removed > 0)
				{
					existRiki.Attachments.Clear();
					existRiki.Attachments.AddRange(keep);
					changed = true;
					MailLogger.Info($"riki souvenir placeholder attachment removed uid={player.Uid} removed={removed} left={existRiki.Attachments.Count}");
				}
			}
		}

		if (!mails.Any((Mail m) => m.Mid == LoveGiftBoxMid))
		{
			Mail mail2 = new Mail
			{
				Mid = LoveGiftBoxMid,
				Title = "爱意礼盒·全员合集",
				Message = LoveGiftBoxMessage,
				Sender = "系统",
				Pid = 0uL,
				Time = now,
				Expiration = ExpireFar,
				Life = 3650u,
				Stat = MailStat.New
			};
			for (uint i = 0u; i < LoveGiftBoxCount; i++)
			{
				mail2.Attachments.Add(NewAttachment(new uint[5] { 4u, 12u, LoveGiftBoxDetailBase + i, 1u, 1u }));
			}
			mails.Add(mail2);
			changed = true;
			MailLogger.Info($"starter gift mails: love gift boxes uid={player.Uid} count={mail2.Attachments.Count}");
		}
		else
		{
			// 2026-11-15（第 330 节）：这封邮件曾被第 328 节改成「附件已直接发放」的空壳（att=0），
			// 现在改回正常形态 —— 附件保留，登录后直接点领取即可。
			// 幂等：只在未领取/未删除且附件不足 16 件时补回，已领取的不会重复发。
			Mail exist = mails.FirstOrDefault((Mail m) => m.Mid == LoveGiftBoxMid);
			if (exist != null && exist.Stat != MailStat.Geted && exist.Stat != MailStat.Removed
				&& exist.Attachments.Count < LoveGiftBoxCount)
			{
				exist.Attachments.Clear();
				for (uint i = 0u; i < LoveGiftBoxCount; i++)
				{
					exist.Attachments.Add(NewAttachment(new uint[5] { 4u, 12u, LoveGiftBoxDetailBase + i, 1u, 1u }));
				}
				exist.Message = LoveGiftBoxMessage;
				exist.Expiration = ExpireFar;
				exist.Life = 3650u;
				changed = true;
				MailLogger.Info($"love gift mail attachments restored uid={player.Uid} count={exist.Attachments.Count}");
			}
		}

		if (changed)
		{
			Save(player, mails);
		}
	}
}
