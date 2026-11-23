using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

internal static partial class MailService
{
	private static readonly Logger MailLogger = new Logger("Mail");

	private const uint MailGid = 999u;

	private const uint MailSid = 1u;

	/// <summary>
	/// 2026-11-15（第 326 节）：邮件过期时间上限。
	/// 坑：客户端原生层把 Expiration 读成 **int32**，4102444800（2100 年）会溢出成 -192522496，
	/// 于是 Mail.IsExpiration() 判为「已过期」，整封邮件被主邮件列表过滤掉（服务端明明推了、客户端 list 里也有，
	/// 就是界面上不显示）。这里取 2037-01-01（2114380800），既远到等同「永久」，又不会越过 int32 上限。
	/// </summary>
	public const uint ExpireFar = 2114380800u;

	private static readonly uint[] WelcomeAttachment = new uint[5] { 4u, 9u, 1u, 3u, 10000u };

	/// <summary>
	/// 2026-11-13 追加（修复记录第 276 节）：欢迎邮件自带的「后勤材料包」。
	/// 口径取自 Resources/item/templates/suplies.json（[Genre, Detail, Particular, Level, Count]）：
	///   虹膜录入证明 ×30、重修额度 / 人事额度 / 特定重修额度 / 重修申请 各 ×99999。
	/// </summary>
	private static readonly uint[][] WelcomeExtra = new uint[5][]
	{
		new uint[5] { 5u, 26u, 1u, 4u, 30u },
		new uint[5] { 5u, 6u, 2u, 1u, 99999u },
		new uint[5] { 5u, 7u, 1u, 1u, 99999u },
		new uint[5] { 5u, 6u, 3u, 1u, 99999u },
		new uint[5] { 5u, 6u, 1u, 2u, 99999u }
	};

	/// <summary>第 329 节起不再单独发这封，标题仅作历史对照（合并进欢迎邮件）。</summary>
	private const string ExtraMailTitle = "系统邮件·后勤材料补给";

	/// <summary>第 329 节起不再单独发这封，标题仅作历史对照（合并进欢迎邮件）。</summary>
	private const string ScoreModelMailTitle = "系统邮件·战场经验补给";

	/// <summary>第 329 节：欢迎邮件固定 Mid（合并后勤材料与战场经验都挂在这封上）。</summary>
	private const ulong WelcomeMid = 1UL;

	/// <summary>
	/// 第 521 节（2026-11-22）：欢迎邮件正文改为「欢迎语 + 免责声明」。
	/// 原文案（整合材料说明）退役；存量邮件由 EnsureWelcomeDisclaimer 迁移。
	/// </summary>
	private const string WelcomeMessage = "欢迎来到尘白禁区S1单机学习版\n"
		+ "【免责声明】—— 尘白禁区 PC 本地服务端（MikuSB S1）\n"
		+ "（1）本服务端及相关文件仅供个人技术学习、逆向研究、程序原理验证用途，仅可在本地单机环境离线运行（仅限本机连接），不涉及任何互联网运营、私服架设、对外提供服务、账号注册、充值盈利等行为。\n"
		+ "（2）《尘白禁区》的全部游戏资源（包括但不限于美术素材、模型、文本、音频、协议结构、UI 等）版权归属于原版权方（西山居 Seasun Games）所有。本项目不发行、不分发原版游戏本体，不破解、不绕过官方正版校验用于营利，游戏客户端需使用者自行通过官方渠道获取。\n"
		+ "（3）使用者明确知晓：本服务端为个人搭建的非官方本地版本，与官方及西山居无关，不存在任何官方客服、官方账号、充值、联机匹配、排行榜等官方服务；服务端内数据（角色、道具、货币等）均为本地测试数据，与官方账号及官方服务器数据完全无关。\n"
		+ "（4）本服务端及相关资源仅供个人学习、研究和技术交流，不保证其完整性、准确性、安全性或适用性。使用者应在学习研究完成后自行删除相关文件，不得长期保存、传播、出售、出租、复制、修改、破解、反向工程或以其他方式用于侵犯他人合法权益的用途。\n"
		+ "（5）使用者自愿承担使用本程序带来的全部风险，包括但不限于设备故障、数据丢失、存档损坏、兼容性报错、账号或存档不可恢复等；项目作者不对使用后果承担任何责任。因使用者违反法律法规、侵犯第三方权益或用于商业用途等行为产生的责任，由使用者自行承担。\n"
		+ "（6）禁止将本服务端用于传播、售卖、搭建公开服务器、面向第三方提供游玩服务，禁止用于任何商业用途。如需体验完整游戏内容，请购买、下载并使用官方正版。如不同意本声明，请立即停止使用并删除全部相关文件。\n"
		+ "（7）本免责声明仅为一般性提示，不构成法律意见，也不能当然免除依法应承担的责任。若相关权利人认为本项目内容侵犯其合法权益，请及时联系，核实后将尽快删除或处理。一旦运行本服务端，即代表你已阅读、理解并同意以上全部免责条款。\n"
		+ "谢谢配合！";

	/// <summary>第 329 节：欢迎邮件已领取/被删时的合并补发标题。</summary>
	private const string WelcomeBundleMailTitle = "系统邮件·后勤材料与战场经验补给";

	/// <summary>
	/// 2026-11-14 新增（修复记录第 299 节）：新手邮件追加的养成材料。
	/// 记忆嵌片与智略点都是 cash.json 里的货币（没有独立物品），邮件附件只能挂物品，
	/// 所以用官方自带的「货币载体物品」发放，领取时在 HandlerReqMailAttachment 里换算成货币：
	///   · 记忆嵌片 = cash 7 (FRAGMENT)。载体「记忆补嵌包」4-6-1-1（token_box，Param1=7、Param2=3），
	///     1 包 = 3 嵌片 → 33333 包 = 99999 嵌片。
	///   · 智略点   = cash 25 (ROGUE3EXPSCIENCE)。载体「智略点」4-1-19-1（cashbox，Param1=25、Param2=1），
	///     1 件 = 1 点 → 99999 件 = 99999 点。
	///   · 武格启芯 = supplies.json G5 D32 P1~P4，依次为 妮塔-悸响 / 晴-霆光 / 猫汐尔-莲驱 / 芙提雅-炬芯，各 3 个。
	///   · 心流数据 = cash 58 (JigsawPuzzleCASH，适格启忆录抽碎片用)。载体「心流数据」4-1-33-1
	///     （cashbox，Param1=58、Param2=1），1 件 = 1 份 → 99999 件 = 99999 份（第 301 节追加）。
	/// </summary>
	public static readonly uint[][] WelcomeStarter = new uint[7][]
	{
		new uint[5] { 4u, 6u, 1u, 1u, 33333u },
		new uint[5] { 4u, 1u, 19u, 1u, 99999u },
		new uint[5] { 4u, 1u, 33u, 1u, 99999u },
		new uint[5] { 5u, 32u, 1u, 1u, 3u },
		new uint[5] { 5u, 32u, 2u, 1u, 3u },
		new uint[5] { 5u, 32u, 3u, 1u, 3u },
		new uint[5] { 5u, 32u, 4u, 1u, 3u }
	};

	/// <summary>
	/// 2026-11-14 追加（修复记录第 311 节）：欢迎邮件再加一件「战场经验」99999。
	/// 口径取自 Resources：
	///   · 战场经验 = cash.json ID 50（Type=ROGUE3SEASONKEY9，Name rogue3.coin_ScoreModel）——**是货币**，
	///     item/templates 里没有独立物品，邮件附件挂不上。
	///   · 官方载体系 others.json [4,1,23,9]（LuaType=cashbox，Param1=50、Param2=1、Icon 1520104），
	///     1 件 = 1 点 → 99999 件 = 99999 点，客户端图标/名称就是「战场经验」。
	/// 领取时由 TryGrantAttachmentCurrency 直接换算成货币（cashbox 的 per=1）。
	/// </summary>
	public static readonly uint[] WelcomeScoreModelExp = new uint[5] { 4u, 1u, 23u, 9u, 99999u };

	/// <summary>
	/// 2026-11-16（第 358 节）：欢迎邮件再加「通行密钥」；第 414 节把数量从 200000 改为 99999。
	/// 通行密钥 = cash.json ID 12（Type=REPRESSSION，I18n supplies.dlc2.token3，图标 1600011），
	/// **是货币**，item/templates 里没有独立物品，邮件附件挂不上。
	/// 官方载体系 others.json [4,1,6,4]（LuaType=cashbox，Param1=12、Param2 空 → 1 件 = 1 个，
	/// 图标 1510108），客户端显示的名称/图标就是「通行密钥」。
	/// 领取时由 TryGrantAttachmentCurrency 直接换算成 cash 12（cashbox 的 per=1），
	/// 不会走 RewardManager 的 Param1 × Count 放大分支。
	/// </summary>
	public static readonly uint[] WelcomePassKey = new uint[5] { 4u, 1u, 6u, 4u, 99999u };

	/// <summary>第 358 节：欢迎邮件已领取/被删时，通行密钥单独补一封用的标题。</summary>
	private const string PassKeyMailTitle = "系统邮件·通行密钥补给";

	/// <summary>
	/// 2026-11-17（第 384 节）：欢迎邮件追加「纪念品」（宿舍家具，G=8）。
	/// 口径取自 item/templates/dorm_gift.txt（前三行是第 385 节追加的同系列活动纪念品）：
	///   · 麻辣火锅营地   = 8-99-10-3（I18n dormgift.gift010，图标 1899010，GMnum=1，庆典活动纪念品）
	///   · 醒狮献瑞       = 8-51-5-3 （I18n dormgift.gift5105，图标 1851005，GMnum=1，夙圆心行-感知巡游）
	///   · 悖论之影       = 8-99-5-3 （I18n dormgift.gift005，图标 1899005，GMnum=1，悖论迷宫）
	///   · 透墨索斯摆件   = 8-99-6-3 （I18n dormgift.gift021，图标 1899006，GMnum=1，悖论迷宫）
	///   · 智略机巧       = 8-99-28-3（I18n dormgift.gift028，图标 1899032，GMnum=1，悖论迷宫）
	/// 发放路径：附件 → IBLogic_BuyGoods.GrantRewardAsync → RewardManager 的 TYPE_HOUSE 分支
	/// → InventoryManager.AddHouseFurnitureItem（按 DormGiftData 校验、按 TemplateId 判重，已持有直接返回 null）。
	/// 因为 GMnum=1、每人只能持有一个，补发幂等要同时看「邮件附件」与「背包」，否则欢迎邮件领完之后
	/// 每次登录都会再补一封。
	/// </summary>
	public static readonly uint[][] WelcomeMemorabilia = new uint[5][]
	{
		new uint[5] { 8u, 99u, 10u, 3u, 1u },
		new uint[5] { 8u, 51u, 5u, 3u, 1u },
		new uint[5] { 8u, 99u, 5u, 3u, 1u },
		new uint[5] { 8u, 99u, 6u, 3u, 1u },
		new uint[5] { 8u, 99u, 28u, 3u, 1u }
	};

	/// <summary>第 384 节：欢迎邮件已领取/被删且背包里也没有时，纪念品单独补一封用的标题。</summary>
	private const string MemorabiliaMailTitle = "系统邮件·纪念品补给";

	/// <summary>
	/// 邮件附件的货币换算：把「货币载体物品」（cashbox / token_box）直接换成货币。
	/// 必须在这里拦一道的原因：
	///   · cashbox 在 RewardManager 里按 Param1 * Count 发（智略点会被放大 25 倍）；
	///   · token_box 在 RewardManager 里完全没有分支，记忆补嵌包只会进背包、嵌片不到账。
	/// 口径：Param1 = cash.json 的货币 ID，Param2 = 每件兑换数量（缺省 1）。
	/// </summary>
	public static bool TryGrantAttachmentCurrency(PlayerInstance player, NtfSyncPlayer sync, SimpleItem att, uint count)
	{
		if (att.G != 4u)
		{
			return false;
		}
		ulong key = GameResourceTemplateId.FromGdpl(att.G, att.D, att.P, att.L);
		OtherItemExcel item;
		if (!GameData.OtherItemByFullId.TryGetValue(key, out item) || item == null)
		{
			return false;
		}
		string luaType = item.LuaType ?? string.Empty;
		if (luaType != "cashbox" && luaType != "token_box")
		{
			return false;
		}
		if (item.Param1 == 0u)
		{
			return false;
		}
		// Param2（每件兑换数量）没有被 Excel 映射进 OtherItemExcel（类里只有 Param1/Param1Raw），
		// 所以这里按物品类型取口径：
		//   · token_box 目前只有「记忆补嵌包」4-6-1-1，others.json 的 Param2=3 → 1 包 = 3 嵌片；
		//   · cashbox（智略点等货币箱）都是 1:1。
		uint per = (luaType == "token_box") ? 3u : 1u;
		player.AddCurrency(item.Param1, per * count, sync);
		MailLogger.Info($"mail attachment cash gdpl={att.G},{att.D},{att.P},{att.L} count={count} -> cash {item.Param1} +{per * count}");
		return true;
	}


	public static List<Mail> Load(PlayerInstance player)
	{
		string text = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 999 && x.Sid == 1)?.Val;
		List<Mail> list = new List<Mail>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return list;
		}
		try
		{
			foreach (string item in JsonSerializer.Deserialize<List<string>>(text) ?? new List<string>())
			{
				list.Add(Mail.Parser.ParseFrom(Convert.FromBase64String(item)));
			}
		}
		catch
		{
		}
		return list;
	}

	public static void Save(PlayerInstance player, List<Mail> mails)
	{
		List<string> value = mails.Select((Mail m) => Convert.ToBase64String(m.ToByteArray())).ToList();
		player.SetStrAttr(999u, 1u, JsonSerializer.Serialize(value));
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	/// <summary>GDPL+数量 → 邮件附件。</summary>
	private static SimpleItem NewAttachment(uint[] gdpl)
	{
		return new SimpleItem
		{
			G = gdpl[0],
			D = gdpl[1],
			P = gdpl[2],
			L = gdpl[3],
			Count = gdpl[4]
		};
	}

	/// <summary>
	/// 2026-11-15（第 326 节）：把溢出到 int32 之外的 Expiration 修回 ExpireFar。
	/// 老数据里写过 4102444800（2100），客户端读成 -192522496 → 判定过期 → 邮件在列表里彻底消失。
	/// 这里顺手自愈，幂等。
	/// </summary>
	private static void EnsureExpirationSafe(PlayerInstance player, List<Mail> mails)
	{
		bool changed = false;
		int fixedCount = 0;
		foreach (Mail m in mails)
		{
			if (m.Expiration > int.MaxValue)
			{
				m.Expiration = ExpireFar;
				changed = true;
				fixedCount++;
			}
		}
		if (changed)
		{
			Save(player, mails);
			MailLogger.Info($"mail expiration clamped to {ExpireFar} uid={player.Uid} count={fixedCount}");
		}
	}

	public static List<Mail> EnsureWelcome(PlayerInstance player)
	{
		List<Mail> list = Load(player);
		if (list.Count > 0)
		{
			EnsureExpirationSafe(player, list);
			// 第 521 节：欢迎邮件正文换成「欢迎语 + 免责声明」，存量邮件一并迁移。
			EnsureWelcomeDisclaimer(player, list);
			// 第 329 节：后勤材料 + 战场经验并进欢迎邮件（原来拆成两封）。
			EnsureWelcomeBundle(player, list);
			// 第 358 节：通行密钥 99999（新号随欢迎邮件下发，老号按下面三条规则补）。
			EnsureWelcomePassKey(player, list);
			// 第 384 节：两件纪念品（麻辣火锅营地 / 醒狮献瑞），老号按同样规则补。
			EnsureWelcomeMemorabilia(player, list);
			// 第 328 节：8 封官方档案邮件不再下发，新号只保留两封礼物邮件。
			EnsureStarterGiftMails(player, list);
			// 第 433 节：生日当天（及之后 90 天）下发「分析员，生日快乐！」。
			EnsureBirthdayMail(player, list);
			return list;
		}
		uint num = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		Mail mail = new Mail
		{
			Mid = WelcomeMid,
			Title = "系统邮件",
			Message = WelcomeMessage,
			Sender = "系统",
			Pid = 0uL,
			Time = num,
			// 第 329 节：邮件里挂了永久养成材料，过期时间与礼物邮件对齐（2037-01-01）。
			Expiration = ExpireFar,
			Life = 3650u,
			Stat = MailStat.New
		};
		mail.Attachments.Add(new SimpleItem
		{
			G = WelcomeAttachment[0],
			D = WelcomeAttachment[1],
			P = WelcomeAttachment[2],
			L = WelcomeAttachment[3],
			Count = WelcomeAttachment[4]
		});
		list.Add(mail);
		foreach (uint[] extra in WelcomeExtra)
		{
			mail.Attachments.Add(new SimpleItem
			{
				G = extra[0],
				D = extra[1],
				P = extra[2],
				L = extra[3],
				Count = extra[4]
			});
		}
		// 第 299 节：记忆嵌片 / 智略点 / 四件武格启芯。
		foreach (uint[] starter in WelcomeStarter)
		{
			mail.Attachments.Add(NewAttachment(starter));
		}
		// 第 311 节：战场经验 99999。
		mail.Attachments.Add(NewAttachment(WelcomeScoreModelExp));
		// 第 358 节：通行密钥 99999。
		mail.Attachments.Add(NewAttachment(WelcomePassKey));
		// 第 384 节：两件纪念品（宿舍家具）。
		foreach (uint[] memo in WelcomeMemorabilia)
		{
			mail.Attachments.Add(NewAttachment(memo));
		}
		Save(player, list);
		EnsureStarterGiftMails(player, list);
		// 第 433 节：生日当天（及之后 90 天）下发「分析员，生日快乐！」。
		EnsureBirthdayMail(player, list);
		return list;
	}

	/// <summary>
	/// 第 521 节（2026-11-22）：存量欢迎邮件正文迁移为「欢迎语 + 免责声明」。
	/// 幂等：正文已是新文案则跳过；欢迎邮件不存在（未发 / 已删）则不动。
	/// </summary>
	private static void EnsureWelcomeDisclaimer(PlayerInstance player, List<Mail> mails)
	{
		Mail? welcome = mails.FirstOrDefault((Mail m) => m.Mid == WelcomeMid);
		if (welcome == null || welcome.Message == WelcomeMessage)
		{
			return;
		}
		welcome.Message = WelcomeMessage;
		Save(player, mails);
		MailLogger.Info($"welcome mail disclaimer migrated uid={player.Uid}");
	}

	/// <summary>
	/// 2026-11-15（第 329 节）：把「后勤材料补给」「战场经验补给」两封并进欢迎邮件。
	///
	/// 需求：系统邮件只留一封 —— 欢迎邮件本身就带比特金 + 后勤材料 + 战场经验，
	/// 整个邮件列表只剩 3 封（欢迎 / 爱意礼盒·全员合集 / 储信终端·纪念品合集）。
	///
	/// 老账号补发也走这一套：
	///   · 欢迎邮件还在（未领取 / 未删除）→ 缺什么就往它身上补什么，不再另开邮件；
	///   · 欢迎邮件已被领取或删除 → 只补 **一封** 合并邮件（绝不拆成两封）。
	///
	/// 幂等：后勤材料看虹膜录入证明 5-26-1-4，战场经验看货币载体 4-1-23-9。
	/// 顺带把欢迎邮件的过期时间提到 ExpireFar（原来只有 30 天，挂永久材料会过期丢件）。
	/// </summary>
	private static void EnsureWelcomeBundle(PlayerInstance player, List<Mail> mails)
	{
		Mail welcome = mails.FirstOrDefault((Mail m) => m.Mid == WelcomeMid);
		bool changed = false;
		if (welcome != null && welcome.Expiration < ExpireFar)
		{
			welcome.Expiration = ExpireFar;
			welcome.Life = 3650u;
			changed = true;
		}
		bool hasExtra = mails.Any(delegate(Mail m)
		{
			return m.Attachments.Any(delegate(SimpleItem a)
			{
				return a.G == 5 && a.D == 26 && a.P == 1 && a.L == 4;
			});
		});
		bool hasScore = mails.Any(delegate(Mail m)
		{
			return m.Attachments.Any(delegate(SimpleItem a)
			{
				return a.G == 4 && a.D == 1 && a.P == 23 && a.L == 9;
			});
		});
		if (hasExtra && hasScore)
		{
			if (changed)
			{
				Save(player, mails);
			}
			return;
		}
		if (welcome != null && welcome.Stat != MailStat.Geted && welcome.Stat != MailStat.Removed)
		{
			int before = welcome.Attachments.Count;
			if (!hasExtra)
			{
				foreach (uint[] extra in WelcomeExtra)
				{
					welcome.Attachments.Add(NewAttachment(extra));
				}
			}
			if (!hasScore)
			{
				welcome.Attachments.Add(NewAttachment(WelcomeScoreModelExp));
			}
			Save(player, mails);
			MailLogger.Info($"welcome bundle merged uid={player.Uid} mid={WelcomeMid} att {before}->{welcome.Attachments.Count}");
			return;
		}
		// 欢迎邮件已领 / 已删：合并补一封（标题沿用旧口径，内含两类附件）。
		ulong mid = (mails.Count == 0) ? 2uL : mails.Max((Mail m) => m.Mid) + 1;
		uint num = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		Mail mail = new Mail
		{
			Mid = mid,
			Title = WelcomeBundleMailTitle,
			Message = "后勤材料补给 + 战场经验补给：虹膜录入证明、各档重修材料与验证战场经验 99999，请查收。",
			Sender = "系统",
			Pid = 0uL,
			Time = num,
			Expiration = ExpireFar,
			Life = 3650u,
			Stat = MailStat.New
		};
		if (!hasExtra)
		{
			foreach (uint[] extra in WelcomeExtra)
			{
				mail.Attachments.Add(NewAttachment(extra));
			}
		}
		if (!hasScore)
		{
			mail.Attachments.Add(NewAttachment(WelcomeScoreModelExp));
		}
		mails.Add(mail);
		Save(player, mails);
		MailLogger.Info($"welcome bundle mail added uid={player.Uid} mid={mid} att={mail.Attachments.Count}");
	}

	/// <summary>
	/// 2026-11-16（第 358 节）：欢迎邮件补「通行密钥」99999 个（第 414 节由 200000 下调）。
	///
	/// 幂等判据：任意一封邮件里已有载体 4-1-6-4 就认为补过了。
	/// 补发规则（沿用第 329 节的三条口径，尽量不增加邮件数量）：
	///   · 欢迎邮件还在（未领取 / 未删除）→ 直接挂到它身上；
	///   · 欢迎邮件已领 / 已删，但 329 节那封合并补给邮件还在 → 挂到那封上；
	///   · 都没有 → 单独补一封「系统邮件·通行密钥补给」。
	/// </summary>
	private static void EnsureWelcomePassKey(PlayerInstance player, List<Mail> mails)
	{
		bool hasPass = mails.Any(delegate(Mail m)
		{
			return m.Attachments.Any(delegate(SimpleItem a)
			{
				return a.G == 4 && a.D == 1 && a.P == 6 && a.L == 4;
			});
		});
		if (hasPass)
		{
			return;
		}
		Mail welcome = mails.FirstOrDefault((Mail m) => m.Mid == WelcomeMid);
		if (welcome != null && welcome.Stat != MailStat.Geted && welcome.Stat != MailStat.Removed)
		{
			welcome.Attachments.Add(NewAttachment(WelcomePassKey));
			Save(player, mails);
			MailLogger.Info($"welcome passkey attached uid={player.Uid} mid={WelcomeMid} att={welcome.Attachments.Count}");
			return;
		}
		Mail bundle = mails.FirstOrDefault((Mail m) => m.Title == WelcomeBundleMailTitle && m.Stat != MailStat.Geted && m.Stat != MailStat.Removed);
		if (bundle != null)
		{
			bundle.Attachments.Add(NewAttachment(WelcomePassKey));
			Save(player, mails);
			MailLogger.Info($"welcome passkey attached uid={player.Uid} mid={bundle.Mid} (bundle) att={bundle.Attachments.Count}");
			return;
		}
		ulong mid = (mails.Count == 0) ? 2uL : mails.Max((Mail m) => m.Mid) + 1;
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		Mail mail = new Mail
		{
			Mid = mid,
			Title = PassKeyMailTitle,
			Message = "通行密钥 99999 个，请查收。\n用于开启危境战幕关卡，每次作战消耗 1 个。",
			Sender = "系统",
			Pid = 0uL,
			Time = now,
			Expiration = ExpireFar,
			Life = 3650u,
			Stat = MailStat.New
		};
		mail.Attachments.Add(NewAttachment(WelcomePassKey));
		mails.Add(mail);
		Save(player, mails);
		MailLogger.Info($"welcome passkey mail added uid={player.Uid} mid={mid} att={mail.Attachments.Count}");
	}

	/// <summary>
	/// 2026-11-17（第 384 节）：欢迎邮件补两件纪念品（麻辣火锅营地 / 醒狮献瑞）。
	///
	/// 幂等判据（两处任一命中即视为已给过）：
	///   · 任意一封邮件的附件里已有该 gdpl —— 还没领、挂在邮件上；
	///   · 背包里已持有该 TemplateId —— 已经领过（欢迎邮件领完就查不到附件了）。
	/// 只判附件会在邮件领取后每次登录重复补一封，所以背包这条必须加。
	///
	/// 补发规则沿用第 329 / 358 节的口径，尽量不新增邮件：
	///   · 欢迎邮件还在（未领取 / 未删除）→ 直接挂到它身上；
	///   · 欢迎邮件已领 / 已删，但合并补给邮件还在 → 挂到那封上；
	///   · 都没有 → 单独补一封「系统邮件·纪念品补给」。
	/// </summary>
	private static void EnsureWelcomeMemorabilia(PlayerInstance player, List<Mail> mails)
	{
		List<uint[]> missing = new List<uint[]>();
		foreach (uint[] memo in WelcomeMemorabilia)
		{
			bool inMail = mails.Any(delegate(Mail m)
			{
				return m.Attachments.Any(delegate(SimpleItem a)
				{
					return a.G == memo[0] && a.D == memo[1] && a.P == memo[2] && a.L == memo[3];
				});
			});
			if (inMail)
			{
				continue;
			}
			ulong templateId = GameResourceTemplateId.FromGdpl(memo[0], memo[1], memo[2], memo[3]);
			bool inBag = player.InventoryManager.InventoryData.Items.Values.Any(x => x.TemplateId == templateId);
			if (inBag)
			{
				continue;
			}
			missing.Add(memo);
		}
		if (missing.Count == 0)
		{
			return;
		}
		Mail welcome = mails.FirstOrDefault((Mail m) => m.Mid == WelcomeMid);
		if (welcome != null && welcome.Stat != MailStat.Geted && welcome.Stat != MailStat.Removed)
		{
			foreach (uint[] memo in missing)
			{
				welcome.Attachments.Add(NewAttachment(memo));
			}
			Save(player, mails);
			MailLogger.Info($"welcome memorabilia attached uid={player.Uid} mid={WelcomeMid} add={missing.Count} att={welcome.Attachments.Count}");
			return;
		}
		Mail bundle = mails.FirstOrDefault((Mail m) => m.Title == WelcomeBundleMailTitle && m.Stat != MailStat.Geted && m.Stat != MailStat.Removed);
		if (bundle != null)
		{
			foreach (uint[] memo in missing)
			{
				bundle.Attachments.Add(NewAttachment(memo));
			}
			Save(player, mails);
			MailLogger.Info($"welcome memorabilia attached uid={player.Uid} mid={bundle.Mid} (bundle) add={missing.Count} att={bundle.Attachments.Count}");
			return;
		}
		ulong mid = ((mails.Count == 0) ? 2uL : (mails.Max((Mail m) => m.Mid) + 1));
		uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		Mail mail = new Mail
		{
			Mid = mid,
			Title = MemorabiliaMailTitle,
			Message = "纪念品补给：麻辣火锅营地、醒狮献瑞、悖论之影、透墨索斯摆件、智略机巧，请查收。\n领用后可在基地小吉处【纪念展区】展示。",
			Sender = "系统",
			Pid = 0uL,
			Time = now,
			Expiration = ExpireFar,
			Life = 3650u,
			Stat = MailStat.New
		};
		foreach (uint[] memo in missing)
		{
			mail.Attachments.Add(NewAttachment(memo));
		}
		mails.Add(mail);
		Save(player, mails);
		MailLogger.Info($"welcome memorabilia mail added uid={player.Uid} mid={mid} add={missing.Count} att={mail.Attachments.Count}");
	}

	public static async Task SendMailsOnLogin(Connection connection)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		List<Mail> mails = EnsureWelcome(player);
		Mail welcome = mails.FirstOrDefault((Mail m) => m.Mid == 1);
		MailLogger.Info($"login mails={mails.Count} welcomeStat={(int)(welcome?.Stat ?? MailStat.Removed)} flag={player.GetAttr(0u, 95u)} money={player.GetAttr(1u, 3u)}");
		if (welcome != null && welcome.Stat == MailStat.Geted && player.GetAttr(0u, 95u) == 0)
		{
			MailLogger.Info("welcome mail already claimed but bitgold not granted; compensating 10000");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			player.AddCurrency(1u, 10000u, sync);
			player.SetAttr(0u, 95u, 1u);
			sync.Custom[player.ToPackedAttrKey(0u, 95u)] = 1u;
			sync.Custom[player.ToShiftedAttrKey(0u, 95u)] = 1u;
			DatabaseHelper.SaveDatabaseType(player.Data);
			await connection.SendPacket(1004, sync, 0);
		}
		foreach (Mail mail in mails)
		{
			if (mail.Stat != MailStat.Removed)
			{
				try
				{
					await connection.SendPacket(1006, mail, 0);
				}
				catch
				{
				}
			}
		}
		await EnsureRogue3DebugParts(connection, player);
	}

	/// <summary>
	/// 2026-11-14（第 315 节）：验证战场「部件」一次性补发。
	///
	/// 需求是「每个品质等级的部件各发 20 个」，单纯靠 GM 命令得等玩家在线再敲一次，
	/// 这里做成登录自补：只在标记（属性组 124 / sid 3499）未置位时发，
	/// 发完立刻置位并落库。之后想要更多就再用 /r3parts [数量]。
	/// 与欢迎邮件无关，纯调试福利；异常全部吞掉，绝不影响登录。
	/// </summary>
	private static async Task EnsureRogue3DebugParts(Connection connection, PlayerInstance player)
	{
		try
		{
			if (Rogue3DUavGrant.AlreadyGranted(player))
			{
				return;
			}
			NtfSyncPlayer sync = new NtfSyncPlayer();
			Rogue3DUavGrant.GrantResult result = Rogue3DUavGrant.GrantEachQuality(player, 20, sync);
			if (result.Total == 0)
			{
				return;
			}
			Rogue3DUavGrant.MarkGranted(player, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			await connection.SendPacket(1004, sync, 0);
			MailLogger.Info($"rogue3 debug parts granted uid={player.Uid} total={result.Total} " +
				$"detail={string.Join(",", result.ByQuality.Select(((int Quality, int Count) x) => $"q{x.Quality}:{x.Count}"))} " +
				$"slots={result.SlotsUsed}/{Rogue3DUavGrant.PartEnd - Rogue3DUavGrant.PartBegin}");
		}
		catch (Exception ex)
		{
			MailLogger.Warn("rogue3 debug parts grant failed: " + ex.Message);
		}
	}

	/// <summary>第 322 节：判断一个 mid 是否属于「官方档案邮件」（MailArchive.cs 里的那批）。</summary>
	public static bool IsArchiveMid(ulong mid)
	{
		return ArchiveMails.Any(((ulong Mid, string Title, string Sender, uint Time, string Body, string[] Items) x) => x.Mid == mid);
	}

	public static ulong ReadMid(byte[] data)
	{
		try
		{
			return PlayerMail.Parser.ParseFrom(data).Mid;
		}
		catch
		{
		}
		try
		{
			return Mail.Parser.ParseFrom(data).Mid;
		}
		catch
		{
		}
		return 0uL;
	}
}
