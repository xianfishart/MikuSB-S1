using System.Collections.Generic;

namespace MikuSB.GameServer.Game.Friend;

/// <summary>
/// 一个假好友的定义。头像用资源模板 ID（0 = 不显示头像）。
/// 生日用「月 / 日」两个数，0 表示这条不展示生日（客户端显示为隐藏）。
/// </summary>
public sealed record FakeFriend(
	ulong Pid,
	string Name,
	string Sign,
	uint Level,
	bool Female,
	ulong FaceTemplateId,
	bool HasVigor,
	int BirthMonth = 0,
	int BirthDay = 0)
{
	/// <summary>是否展示生日（月日都合法才展示）。</summary>
	public bool ShowBirthday => BirthMonth >= 1 && BirthMonth <= 12 && BirthDay >= 1 && BirthDay <= 31;
}

/// <summary>
/// 假好友总表（2026-11-11 新增）。
/// 原来 1016（好友列表）、1019（好友体力状态）、35/37（赠送/领取体力）
/// 各自硬编码一份 id，加人时要改三处、极易漏改。现在全部从这张表派生：
/// 想加好友只在 All 里加一行。
/// PID 从 9000001 开始，玩家真实 PID 是自增小数字，不会撞号。
///
/// 头像 ID 说明（2026-11-11 三次修正）：
/// 个人资料「头像」用的物品模板 ID 由 MikuSB.Data.GameResourceTemplateId.FromGdpl 打包：
///   id = level &lt;&lt; 48 | particular &lt;&lt; 32 | detail &lt;&lt; 16 | genre
/// 注意 particular 在 detail 之上（2026-11-11 前两次把这两个位搞反，
/// 导致客户端查不到模板、好友列表回落到默认头像、详情页空白）。
/// 头像类固定 genre=9、detail=2、level=1，particular 是模板表里的 Particular 字段。
/// 序号与名称取自 Resources/item/templates/profile.json（LuaType=profile_face）
/// 与 Resources/language/profile.json（Key 形如 profile.drawXXX，中文名「迷你某某」）。
/// </summary>
public static class FakeFriendTable
{
	/// <summary>不显示头像。</summary>
	public const ulong FaceNone = 0uL;

	// 各角色的「迷你」头像，按 MikuSB.Data.GameResourceTemplateId.FromGdpl 的真实公式算：
	//   id = level<<48 | particular<<32 | detail<<16 | genre
	// 迷你头像固定 genre=9、detail=2、level=1，particular 才是头像序号
	// （Resources/item/templates/profile.json 里的 Particular 字段，不是 I18n 里的 drawXXX 编号）。
	public const ulong FaceDefault = 281479271809033uL;   // P=1   迷你分析员
	public const ulong FaceAnka = 281496451678217uL;      // P=5   迷你安卡希雅
	public const ulong FaceFenni = 281500746645513uL;     // P=6   迷你芬妮
	public const ulong FaceFutiya = 281505041612809uL;    // P=7   迷你芙提雅
	public const ulong FaceLifu = 281509336580105uL;      // P=8   迷你里芙
	public const ulong FaceYao = 281513631547401uL;       // P=9   迷你肴
	public const ulong FaceQinnuo = 281517926514697uL;    // P=10  迷你琴诺
	public const ulong FaceSerisi = 281522221481993uL;    // P=11  迷你瑟瑞斯
	public const ulong FaceChenxing = 281526516449289uL;  // P=12  迷你辰星
	public const ulong FaceQing = 281530811416585uL;      // P=13  迷你晴
	public const ulong FaceMolian = 281535106383881uL;    // P=14  迷你茉莉安
	public const ulong FaceMaoxier = 281539401351177uL;   // P=15  迷你猫汐尔
	public const ulong FaceEnya = 281543696318473uL;      // P=16  迷你恩雅
	public const ulong FaceNita = 281547991285769uL;      // P=17  迷你妮塔
	public const ulong FaceAida = 281586645991433uL;      // P=26  埃达-微笑练习
	public const ulong FaceTaisi = 281590940958729uL;     // P=27  迷你苔丝
	public const ulong FaceKaisiya = 281599530893321uL;   // P=29  迷你凯茜娅
	public const ulong FaceYiqieer = 281621005729801uL;   // P=34  迷你伊切尔
	public const ulong FaceVidiya = 281676840304649uL;    // P=47  迷你薇蒂雅
	public const ulong FaceBubu = 281732674879497uL;      // P=60  迷你卜卜
	public const ulong FaceNailide = 281814279258121uL;   // P=79  迷你奈莉德
	public const ulong FaceKeluoruina = 281904473571337uL; // P=100 迷你克罗瑞娜
	public const ulong FaceMiya = 281921653440521uL;      // P=104 迷你米娅

	// 胧嫣没有「迷你」头像，改用它自己的卡面「胧嫣-瑞狐」
	// （模板 profile card.girl241_suits，genre=9 / detail=1 / particular=24 / level=1）。
	public const ulong FaceLongyan = 281578055991305uL;

	// 卡罗琳是 NPC，没有「迷你」头像也没有可用卡面，走 NPC 头像这一档：
	// 模板 profile.npc005， genre=9 / detail=3 / particular=5 / level=1（Icon=1900015）。
	public const ulong FaceCaroline = 281496451743753uL;

	/// <summary>
	/// 好友总表。出生日期取自游戏内档案 Resources/language/riki.json 的
	/// 「通称：X / 生日：M月D日」（里芙在档案里的通称是「星期三」= 3月21日）。
	/// 埃达是 AI 助手，档案里没有生日，留 0 表示不展示。
	/// </summary>
	public static readonly IReadOnlyList<FakeFriend> All = new FakeFriend[]
	{
		new FakeFriend(9000001uL, "里芙", "今天也要一起加油。", 100u, true, FaceLifu, true, 3, 21),
		new FakeFriend(9000002uL, "芬妮", "别拖我后腿哦。", 100u, true, FaceFenni, true, 7, 30),
		new FakeFriend(9000003uL, "肴", "肴肴领先~", 100u, true, FaceYao, true, 12, 23),
		new FakeFriend(9000004uL, "辰星", "我希望我们能以理性，而非感性来解决合作上的困难", 100u, true, FaceChenxing, true, 11, 5),
		new FakeFriend(9000005uL, "苔丝", "表演开始喽，当当！", 100u, true, FaceTaisi, true, 9, 16),
		new FakeFriend(9000006uL, "凯茜娅", "别急啊，我们...有的是时间", 100u, true, FaceKaisiya, true, 11, 8),
		new FakeFriend(9000007uL, "安卡希雅", "恋爱游戏大神~", 100u, true, FaceAnka, true, 12, 20),
		new FakeFriend(9000008uL, "茉莉安", "我想要的疼痛，只有亲爱的...", 100u, true, FaceMolian, true, 9, 5),
		new FakeFriend(9000009uL, "晴", "听从您的指令", 100u, true, FaceQing, true, 8, 20),
		new FakeFriend(9000010uL, "芙提雅", "干什么！", 100u, true, FaceFutiya, true, 4, 23),
		new FakeFriend(9000011uL, "恩雅", "想要撒娇的话，随时都可以哦", 100u, true, FaceEnya, true, 1, 19),
		new FakeFriend(9000012uL, "妮塔", "好兄弟", 100u, true, FaceNita, true, 5, 5),
		new FakeFriend(9000013uL, "琴诺/莫尔索", "为了您，我什么都能做到", 100u, true, FaceQinnuo, true, 2, 27),
		new FakeFriend(9000014uL, "猫汐尔", "谁才是好喵~", 100u, true, FaceMaoxier, true, 10, 10),
		new FakeFriend(9000015uL, "瑟瑞斯", "今天要做的事...还有...", 100u, true, FaceSerisi, true, 7, 19),
		new FakeFriend(9000016uL, "伊切尔", "肚子饿了，分析员，有吃的吗？", 100u, true, FaceYiqieer, true, 11, 29),
		new FakeFriend(9000017uL, "薇蒂雅", "分析员...嘿嘿", 100u, true, FaceVidiya, true, 6, 14),
		new FakeFriend(9000018uL, "卜卜", "有分析员陪着，卜卜一定能成为厉害的大天师", 100u, true, FaceBubu, true, 1, 31),
		new FakeFriend(9000019uL, "奈莉德", "冥河代理人", 100u, true, FaceNailide, true, 12, 31),
		new FakeFriend(9000020uL, "克罗瑞娜", "还想要更多，你的味道", 100u, true, FaceKeluoruina, true, 3, 13),
		new FakeFriend(9000021uL, "米娅", "偷袭成功~", 100u, true, FaceMiya, true, 8, 15),
		// 陇嫣（游戏内正名「胧嫣」）：没有迷你头像，用她自己的卡面「胧嫣-瑞狐」。
		new FakeFriend(9000022uL, "陇嫣", "妾身的时间，全部属于你", 100u, true, FaceLongyan, true, 2, 1),
		new FakeFriend(9000023uL, "萨菲尔", "欸嘿~", 100u, true, FaceAida, true),
		// 卡罗琳：NPC 头像，档案无生日。
		new FakeFriend(9000024uL, "卡罗琳", "才不是热水壶！", 100u, true, FaceCaroline, true)
	};
}
