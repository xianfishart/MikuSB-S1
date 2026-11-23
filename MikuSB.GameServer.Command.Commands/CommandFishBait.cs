using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 新星开拓「鱼饵」九种一套，默认每样 10000 个（2026-11-15 新增，修复记录第 344/345 节）。
///
/// **鱼饵不是背包道具，是 Cash（货币）** —— 这是踩了两版才确认的：
///   /itemfind bait1 在服务端数据里命中的是 [CashData] CashExcel，key 就是货币号：
///     万能鱼饵          VIRCAPTUREBAIT1        cash 38
///     高级鱼饵          VIRCAPTUREBAIT2        cash 39
///     普通鱼饵          VIRCAPTURENORMALBAIT1  cash 43
///     豪华鱼饵          VIRCAPTURENORMALBAIT2  cash 44
///     至尊鱼饵          VIRCAPTURENORMALBAIT3  cash 45
///     重量级鱼类虫饵    VIRCAPTUREMATCHBAIT1   cash 46
///     巨型鱼类虫饵      VIRCAPTUREMATCHBAIT2   cash 47
///     重量级鱼类夜钓虫饵 VIRCAPTUREMATCHBAIT3  cash 48
///     巨型鱼类夜钓虫饵  VIRCAPTUREMATCHBAIT4   cash 49
///   others.json 里那 9 条 [4,1,6,Level] 只是客户端展示用的道具壳，
///   Param1 正好就是对应的 cash 号（38/39/43~49）。
///
/// 所以发放必须走 Player.AddCurrency(cashId, count)，落到属性 (1, 2*cashId+1)，
/// 客户端钓鱼界面的鱼饵数量读的就是这组属性。之前两版都发错了：
///   第 1 版 GrantRewardAsync —— LuaType=cashbox 且 Param1≠0，被 RewardManager 当成资源箱
///     执行 AddCurrency(Param1, Param1 * count)，数量被乘了 38 倍且落在同一个货币上；
///   第 2 版 AddOtherItem 直接塞 InventoryData.Items —— 客户端根本不认这些壳道具，
///     既不显示也占不到鱼饵额度，看起来就是「点了没反应」。
///
/// 注意 4-1-6 这一段还混着好伙伴券(Level 7)、经验盒(8/11/12/13)、新星币(14)、
/// 建筑·增益解锁券(16/17)，只能靠 Level 精确区分。
/// </summary>
[CommandInfo("fishbait", "Give all 9 kinds of VirCapture fishing bait to target.", "Usage: /fishbait [count=10000]  (append @uid)", new string[]
{
	"bait",
	"yuer",
	"baits"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandFishBait : ICommands
{
	/// <summary>九种鱼饵的 [Cash 货币号, 对应 others.json 的 Level, 名称]。</summary>
	private static readonly (uint Cash, uint Level, string Name)[] Baits = new (uint, uint, string)[]
	{
		(38u, 9u, "万能鱼饵"),
		(39u, 10u, "高级鱼饵"),
		(43u, 40u, "普通鱼饵"),
		(44u, 41u, "豪华鱼饵"),
		(45u, 42u, "至尊鱼饵"),
		(46u, 43u, "重量级鱼类虫饵"),
		(47u, 44u, "巨型鱼类虫饵"),
		(48u, 45u, "重量级鱼类夜钓虫饵"),
		(49u, 46u, "巨型鱼类夜钓虫饵")
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
		int granted = 0;
		StringBuilder sb = new StringBuilder();
		foreach (var bait in Baits)
		{
			player.AddCurrency(bait.Cash, (uint)count, sync);
			granted++;
			if (sb.Length > 0)
			{
				sb.Append('、');
			}
			sb.Append(bait.Cash).Append('=').Append(player.GetAttr(1u, 2u * bait.Cash + 1u));
		}
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"fishbait: 九种鱼饵各 +{count}（cash 号:现存量 {sb}，uid {player.Uid}，共 {granted} 种）。");
	}
}
