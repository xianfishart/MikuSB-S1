using System;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 推币大赢家（ArcadeGame / CoinPusher / 基地街机）等级设置
/// （2026-11-13 新增，修复记录第 267 节）。
/// 网页面板「一键满级推币大赢家」按钮调 <c>/arcadelevel @uid</c>，不带参数即满级。
///
/// 这个玩法的等级**不落库**：客户端 HouseArcadeToSeverMsg:GetLevel() 拿累计经验
/// 101/18002 去比 Resources/house/coinpusher/coinpusher_rewards.json 的 Exp 阈值，
/// 逐级扣减，扣不动就是当前级。所以「满级」= 把 18002 写成 1~max-1 级阈值之和
/// （当前 200+400+600+800+1000×10 = 12000）。
/// 只改经验，**不碰领奖位图 18003**：满级后仍按界面正常一键领取 1~15 级奖励。
/// </summary>
[CommandInfo("arcadelevel", "Set 推币大赢家 (CoinPusher) to max level via exp.", "Usage: /arcadelevel [level]  (append @uid; no value = max)", new string[]
{
	"arcademax",
	"coinpusher",
	"coinpusherlevel"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandArcadeLevel : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int max = ArcadeGameHelper.MaxLevel();
		if (max <= 0)
		{
			await arg.SendMsg("arcadelevel: Resources/house/coinpusher/coinpusher_rewards.json 缺失或为空，取不到满级等级。");
			return;
		}
		int exp = ArcadeGameHelper.ReadExp(player);
		int oldLevel = ArcadeGameHelper.LevelFromExp(exp);
		int targetLevel = arg.GetInt(0);
		if (targetLevel <= 0 || targetLevel > max)
		{
			targetLevel = max;
		}
		int targetExp = (targetLevel >= max) ? ArcadeGameHelper.MaxExp() : ExpFor(targetLevel);
		if (oldLevel == targetLevel && exp == targetExp)
		{
			await arg.SendMsg($"arcadelevel: 推币大赢家已是 Lv{oldLevel}/{max}（累计经验 {exp}），无需改动。uid {player.Uid}。");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		await HouseAttr.SetAsync(arg.Target, ArcadeGameHelper.ExpSid, (uint)Math.Max(0, targetExp), sync);
		await player.SendPacket(1004, sync);
		// 1004 之后再来一条 1010 ExtraSync，界面才会立刻重画（同 watergun / throwlevel 的做法）。
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		// 属性改在内存里，必须显式落库，否则服务器重启会退回旧等级。
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"arcadelevel: 推币大赢家 Lv{oldLevel}(exp {exp}) -> Lv{targetLevel}(exp {targetExp})，满级 Lv{max}。uid {player.Uid}。重开推币大赢家界面即可生效；等级奖励仍走界面正常领取（1~{targetLevel} 级都能领）。");
	}

	/// <summary>升到 level 级所需的累计经验（1~level-1 的阈值之和）。</summary>
	internal static int ExpFor(int level)
	{
		int sum = 0;
		foreach (ArcadeGameHelper.Row row in ArcadeGameHelper.LoadRows())
		{
			if (row.Level < level && row.Exp > 0)
			{
				sum += row.Exp;
			}
		}
		return sum;
	}
}
