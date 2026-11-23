using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

/// <summary>
/// 2026-11-19（修复记录第 433 节）：生日邮件。
///
/// 现象：游戏里设置生日后，生日当天收不到「分析员，生日快乐！」这封信。
///
/// 排查结论：
///   · 客户端有一整套生日邮件逻辑 —— BirthdayMail 模块（tbWishMailConf[2025]/[2026]、InBirthday /
///     InBirthdayPeriod / CheckNew / CheckShow），Mail.HandleBirthDayMail 负责把带 year 的邮件
///     改写成 mailid = -year 的客户端虚拟邮件，领取时回调 Activity_BirthdayMail_Gain 写 attr 155/年份。
///   · 但邮箱列表（UI mail 的 tbMails）只吃服务端下发的 1006 邮件，客户端那套虚拟邮件在本版本里
///     没有任何入口会把它塞进列表 —— 实测打开邮箱、重登都不会触发，所以界面里永远看不到。
///   · 官方原文和附件其实就在客户端自带的 247 封邮件档案 _G.Mail.sFavoritesMail 里：
///       2025 年：标题「分析员，生日快乐！」发件人「最爱你的她们」
///                附件 4-99-20-1-1 / 4-3-2-1-80 / 4-2-1-7-2
///       2026 年：同一标题，正文换成「今天，大家都早早完成了任务…」
///                附件 4-99-60-1-1 / 4-3-2-1-80 / 4-2-1-7-2
///
/// 因此这里按官方档案的原文、原附件，在生日当天（以及生日后 90 天的领取期内补发）由服务端
/// 真正下发一封普通邮件。标题、正文、发件人、附件都与官服一致。
///
/// 幂等：
///   · Mid = MidBase + year，同一年的信只发一次；邮件被删也不会重复补（155/年份 已置位）。
///   · 发送后把 attr 155/年份 置 1，既挡住客户端那套虚拟邮件的重复展示，也作为「本年已发」的标记。
/// </summary>
internal static partial class MailService
{
	private const ulong BirthdayMailMidBase = 9600000000UL;

	private const string BirthdayMailTitle = "分析员，生日快乐！";

	private const string BirthdayMailSender = "最爱你的她们";

	/// <summary>生日后仍可领取的天数，与客户端 tbWishMailConf[2026].nPeriodTime 一致。</summary>
	private const int BirthdayClaimDays = 90;

	private const string BirthdayMailBody2025 =
		"亲爱的分析员：\n\n"
		+ "祝您生日快乐~ \n\n"
		+ "您是一切故事的开端，在未来的每个篇章，请握紧我们的手，继续书写下爱意与幸福。\n\n"
		+ "不只是今年，还有明年，以及每一年，我们都要相聚在此，为您庆祝这特别的日子~ \n\n"
		+ "许个愿吧，分析员，大家会一起为您实现的。因为您的期盼，就是我们共同的心之所向。 \n\n"
		+ "——海姆达尔全体少女";

	private const string BirthdayMailBody2026 =
		"亲爱的分析员： \n\n"
		+ "今天，大家都早早完成了任务，想要陪您一整天，想要为您送上第一声生日快乐，想要......合影时抢占距离您最近的位置~\n\n"
		+ "所以，房间被我们折腾得有点乱，一会儿也许还会更乱^^但你说过，喜欢一家人聚在一起那种热热闹闹的感觉，对吧？ \n\n"
		+ "总之，要幸福，要平安，要为我们照顾好自己。往后每个寻常的晨昏，每次出行，每次归家，我们都会像现在这样，紧紧挽住您的手臂。\n\n"
		+ "接下来，该轮到每个人用自己的方式为您庆祝了，分析员想先体验哪一个？可以多选哦。 \n\n"
		+ "——海姆达尔全体少女";

	/// <summary>官方档案里的附件口径（GDPL + 数量）。</summary>
	private static readonly uint[][] BirthdayItems2025 = new uint[3][]
	{
		new uint[5] { 4u, 99u, 20u, 1u, 1u },
		new uint[5] { 4u, 3u, 2u, 1u, 80u },
		new uint[5] { 4u, 2u, 1u, 7u, 2u }
	};

	private static readonly uint[][] BirthdayItems2026 = new uint[3][]
	{
		new uint[5] { 4u, 99u, 60u, 1u, 1u },
		new uint[5] { 4u, 3u, 2u, 1u, 80u },
		new uint[5] { 4u, 2u, 1u, 7u, 2u }
	};

	/// <summary>
	/// 生日邮件补发。生日当天登录（或生日后 90 天内第一次登录）时下发；
	/// 生日没设置（属性 200/1、200/2 为 0）直接跳过。
	/// </summary>
	public static void EnsureBirthdayMail(PlayerInstance player, List<Mail> mails)
	{
		try
		{
			uint month = player.GetAttr(200u, 1u);
			uint day = player.GetAttr(200u, 2u);
			if (month < 1 || month > 12 || day < 1 || day > 31)
			{
				return;
			}
			DateTime today = DateTime.Now.Date;
			// 生日可能在今年，也可能在去年年末（跨年 90 天窗口内）。
			for (int back = 0; back <= 1; back++)
			{
				int year = today.Year - back;
				DateTime bd = BirthdayDate(year, month, day);
				if (bd > today)
				{
					continue;
				}
				if ((today - bd).TotalDays > BirthdayClaimDays)
				{
					continue;
				}
				SendBirthdayMail(player, mails, (uint)year);
				return;
			}
		}
		catch (Exception ex)
		{
			MailLogger.Warn($"birthday mail check failed uid={player.Uid}: {ex.Message}");
		}
	}

	/// <summary>2/29 生日在平年按 2/28 算，与客户端 InBirthday 的判定保持一致。</summary>
	private static DateTime BirthdayDate(int year, uint month, uint day)
	{
		int d = (int)day;
		int max = DateTime.DaysInMonth(year, (int)month);
		if (d > max)
		{
			d = max;
		}
		return new DateTime(year, (int)month, d);
	}

	private static void SendBirthdayMail(PlayerInstance player, List<Mail> mails, uint year)
	{
		ulong mid = BirthdayMailMidBase + year;
		Mail existing = mails.FirstOrDefault((Mail m) => m.Mid == mid);
		if (existing != null)
		{
			// 老存档可能只有客户端的「已领」标记（attr 155/年份），但没有真正的邮件；
			// 这里以邮件本体为准，补齐标记即可，不重复发信。
			if (player.GetAttr(155u, year) == 0)
			{
				player.SetAttr(155u, year, 1u);
			}
			return;
		}
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		bool is2026 = year >= 2026;
		Mail mail = new Mail
		{
			Mid = mid,
			Title = BirthdayMailTitle,
			Message = is2026 ? BirthdayMailBody2026 : BirthdayMailBody2025,
			Sender = BirthdayMailSender,
			Pid = 0uL,
			Time = now,
			Expiration = now + (uint)BirthdayClaimDays * 86400u,
			Life = (uint)BirthdayClaimDays,
			Stat = MailStat.New
		};
		foreach (uint[] item in is2026 ? BirthdayItems2026 : BirthdayItems2025)
		{
			mail.Attachments.Add(NewAttachment(item));
		}
		mails.Add(mail);
		player.SetAttr(155u, year, 1u);
		Save(player, mails);
		MailLogger.Info($"birthday mail sent uid={player.Uid} year={year} mid={mid} items={mail.Attachments.Count}");
	}
}
