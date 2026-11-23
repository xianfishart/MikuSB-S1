using System.Collections.Generic;
using System.Linq;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

/// <summary>
/// 2026-11-14（第 321 节）：储信终端「官方邮件」按客户端自带档案还原。
///
/// 数据来源：客户端 _G.Mail.sFavoritesMail —— 一个 1.5MB 的 JSON，内含 247 封官方邮件的
/// 完整档案（mailid / 多语言标题 / 发件人 / 正文 / 附件 / 收发时间）。储信终端渲染纪念邮件
/// 就是读这份档案，所以它就是官服那批邮件的原文，不必再手抄截图。
///
/// 语音邮件：正文首行的 ![audio](audio_xx_yyyy.brief) 是语音标记，客户端 Mail.ParseContent 会把它
/// 切成 audio 块并显示语音条，随后从 {Login.GetContent()}mail/audio/<brief> 取时长清单、
/// 再从 .../mail/resource/<mp3> 取音频。该 CDN 可直连，所以服务端只需把标记原样写进正文。
///
/// Mid 直接用官方 mailid，方便以后按 id 从档案里继续追加。
/// </summary>
internal static partial class MailService
{
	private static readonly (ulong Mid, string Title, string Sender, uint Time, string Body, string[] Items)[] ArchiveMails =
		new (ulong, string, string, uint, string, string[])[8]
	{
		(1270255uL, "姐姐来咯！", "恩雅", 1705593650u,
			"分析员，我已经收到你的生日礼物和祝福了哦，我很喜欢！分析员亲手做这个毛绒玩具，应该花了不少时间吧，我会每天都抱着它睡觉的~\n福利院的孩子们说，要给我准备生日大餐。如果分析员有空的话，我想让你陪我一起去，毕竟孩子们也很喜欢你嘛。\n说好了的话，我们就在公司门口会合吧。",
			new string[2] { "4,1,6,5,80", "4,3,1,1,12000" }),
		(1270086uL, "一点见面礼", "凯茜娅", 1705571425u,
			"你果然还是走到了这里，海姆达尔的分析员，看来耶洛沙的寒风和那些流言并不能让你退却。勇者总该获得一些嘉奖，收下吧，也作为庆祝我们重逢的小礼物。\n现在，收起对过去的留念，打起十二分的精神，准备好面对更真实的耶洛沙吧。",
			new string[1] { "5,21,1,4,10" }),
		(1179501uL, "新年快乐！", "苔丝", 1704038450u,
			"分析员，起床了吗？今天可是新年的第一天，赖床的话一年都会懒洋洋的哦。而且，你之前答应了要和我一起出去买东西，这次可不能突然消失了！\n\n哎嘿嘿，我从昨天就已经在写购物清单了，有什么想要的东西，记得提前发给我哦。\n\n新年新气象，今年也一起努力吧！",
			new string[3] { "4,2,1,7,5", "4,3,1,1,50000", "4,3,2,1,500" }),
		(1154144uL, "海姆达尔休假团的来信", "系统", 1702990853u,
			"谢谢你能陪我们一起玩游戏！\n没想到休假刚结束就发生了这么多事，也不知道分析员的近况如何？突然到那么冷的地方还适应吗？\n这是大家之前一起为你制作的纪念品，还请当做护身符带在身边，务必要从耶洛沙平安归来。",
			new string[1] { "10,1,20,1,1" }),
		(914446uL, "假日补给", "卡罗琳", 1696125650u,
			"分析员，最近作战辛苦了，任务完成得很出色，不愧是你。\n\n公司为海姆达尔特批了长假，还准备了假日补给，至于怎么分配，就由分析员来决定了，毕竟天启者的员工幸福指数也是分析员需要关注的哦。\n\n好了好了，放假就不谈工作了，分析员，尽情享受和队员们的闲暇时光吧～",
			new string[3] { "4,1,6,5,200", "4,3,1,1,100000", "4,2,1,7,2" }),
		(912921uL, "一起去赏月吧！", "辰星", 1695952850u,
			"分析员，不知你是否留意到，最近几天的月亮都十分明亮。\n在我的家乡，这意味着到了离家游子归家团圆的时候。但因为各种原因，我已经很久没有回去，家中也已经没有让我挂念的人了。\n在来到海姆达尔部队，遇到了你之后，我才重新有了回家的感觉。如果可以的话，在今天，我希望与你一同度过。\n天气预报说今天是晴天，我想，今晚的月色，应该会很美。",
			new string[4] { "4,3,1,1,81500", "5,1,1,3,8", "5,2,1,3,8", "5,3,1,3,8" }),
		(833767uL, "来自芬妮的私信", "系统", 1692684000u,
			"分析员，你听说过最近的热门打卡“圣地”吗？\n可以欣赏星河的花园天幕餐厅、湾区告白摩天轮、海姆达尔部队基地的楼顶天台……\n我说，这该不会是写推荐的人在夹带私货吧！\n还有，零区赏“雪”？\n唔，虽然和本小姐预想的不太一样，不过看起来还挺别致的。\n其实，只要这一天能和分析员一起度过，无论是什么地方都会成为我心中的“圣地” 。\n好啦，我就在这里等你，可不要让本小姐等太久哦！",
			new string[5] { "4,1,6,5,200", "4,3,1,1,77777", "5,1,1,3,7", "5,2,1,3,7", "5,3,1,3,7" }),
		(14578755uL, "光阴的誓约", "安卡希雅", 1771257623u,
			"![audio](audio_46_1770890374327.brief)\n分析员，全能测试官，专属玩家？现在是，亲，爱，的。\n\n以后，要尽快习惯这个称呼哦——诶不对，被叫亲爱的什么的，这方面你才是高手高手高高手......但这是专属于我们的完美结局，对吧？跟打“魄类”游戏时心态炸裂想要放弃不同，我是从某一刻起，就抱着坚定的决心，一定要与你达成这个成就的呢。\n\n你问我具体是“哪一刻”，我好像从没提过，从休眠仓醒来见到你的第一眼，曾闪过一个念头：被存档的不是时间，而是为了相遇的等待。\n\n这份悸动，被最初的迷茫暂时掩盖过去，又在你与我经历的真实冒险里，在你为我调试游戏参数的侧脸里，在你写给未来的安卡希雅的明信片里，在与你共度的每一天里，变得愈发清晰有力，震动了我的世界。所以，这个问题，就由过去，现在，未来的每一刻，来替我回答吧。\n\n对了，刚才少说了一个最重要的称呼——新年快乐，我的挚爱。",
			new string[7] { "5,6,2,1,1", "5,6,1,2,1", "4,2,1,7,2", "4,1,6,5,30", "4,3,1,1,30000", "5,3,1,4,2", "5,9,1,2,3" })
	};

	/// <summary>按 mid 幂等补发档案邮件；Expiration 给 2100，永久不过期。</summary>
	private static void EnsureArchiveMails(PlayerInstance player, List<Mail> mails)
	{
		int added = 0;
		foreach ((ulong mid, string title, string sender, uint time, string body, string[] items) in ArchiveMails)
		{
			if (mails.Any((Mail m) => m.Mid == mid))
			{
				continue;
			}
			Mail mail = new Mail
			{
				Mid = mid,
				Title = title,
				Message = body,
				Sender = sender,
				Pid = 0uL,
				Time = time,
				Expiration = ExpireFar,
				Life = 3650u,
				Stat = MailStat.New
			};
			foreach (string raw in items)
			{
				string[] f = raw.Split(',');
				mail.Attachments.Add(new SimpleItem
				{
					G = uint.Parse(f[0]),
					D = uint.Parse(f[1]),
					P = uint.Parse(f[2]),
					L = uint.Parse(f[3]),
					Count = uint.Parse(f[4])
				});
			}
			mails.Add(mail);
			added++;
		}
		if (added > 0)
		{
			Save(player, mails);
			MailLogger.Info($"archive mails added uid={player.Uid} count={added} total={mails.Count}");
		}
	}
}