using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 猜心对局（GuessPoker）经验累加（2026-11-12 新增，修复记录第 257 / 272 节）。
/// 网页面板「猜心对局 +1320 经验」按钮调 <c>/guesspoker 1320 @uid</c>，点一次加一次。
///
/// 口径与 House 侧 <see cref="PubAttr"/> 完全一致，不另写一份：
///   - 等级本身不落库，只有经验存在属性 Gid=101 / Sid=16001（PubAttr.Sid(1u)）；
///   - 等级由 PubAttr.ResolveLevel(exp) 按 Resources/house/GuessPoker/GuessPokerRewards.json 换算；
///   - 阶梯 1:240 2:300 3:360 4:420 5:660 6:800 7:1060 8:1200 9:1360 10:1460
///          11:1600 12:1720 13:1850 14:2000 15:2200（共 15 级）；
///   - N 级起点经验 = Exp[1] + ... + Exp[N-1]，满级（15 级）累计 15030。
/// 所以这里直接对经验做加法，等级交给 ResolveLevel 换算，避免两套口径打架。
/// </summary>
[CommandInfo("guesspoker", "Add GuessPoker (猜心对局) exp to the target player.", "Usage: /guesspoker <exp>  (append @uid; no value = 1320)", new string[]
{
	"poker"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandGuessPoker : ICommands
{
	/// <summary>猜心对局经验所在的属性位。</summary>
	public const uint AttrGid = 101u;

	/// <summary>网页按钮每次点击增加的默认经验。</summary>
	public const uint DefaultGain = 1320u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		int add = arg.GetInt(0);
		if (add <= 0)
		{
			add = (int)DefaultGain;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		Dictionary<uint, (uint Exp, List<uint> Reward)> ladder = PubAttr.Ladder();
		if (ladder.Count == 0)
		{
			await arg.SendMsg("guesspoker: Resources/house/GuessPoker/GuessPokerRewards.json 缺失或为空，无法换算等级。");
			return;
		}
		uint top = ladder.Keys.Max();
		// 满级累计经验：累加 1..top-1 每级的升级需求（最后一级的 Exp 不再需要）。
		uint cap = 0u;
		for (uint i = 1u; i < top; i++)
		{
			if (ladder.TryGetValue(i, out var row))
			{
				cap += row.Exp;
			}
		}
		uint sid = PubAttr.Sid(1u);
		uint oldExp = player.GetAttr(AttrGid, sid);
		uint oldLevel = PubAttr.ResolveLevel(oldExp);
		if (oldExp >= cap)
		{
			await arg.SendMsg($"guesspoker: 已是满级 Lv{top}（exp {oldExp}/{cap}），无需再加。uid {player.Uid}。");
			return;
		}
		long sum = (long)oldExp + add;
		uint exp = (sum >= cap) ? cap : (uint)sum;
		uint level = PubAttr.ResolveLevel(exp);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		await PubAttr.SetAttrAsync(arg.Target, sid, exp, sync);
		await player.SendPacket(1004, sync);
		if (arg.Target is Connection connection)
		{
			// 1004 之后再来一条 1010 ExtraSync，界面才会立刻重画（同 money 命令的做法）。
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
		await arg.SendMsg($"guesspoker: exp {oldExp} -> {exp} (+{exp - oldExp}), Lv{oldLevel} -> Lv{level} (max Lv{top}, cap {cap}) uid {player.Uid}. 重开猜心对局界面即可看到新等级。");
	}
}
