using System;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 心动水弹（LoveGun / 温泉中心水枪）等级设置（2026-11-12 新增，修复记录第 259 节）。
/// 网页面板「心动水弹一键满级」按钮调 <c>/watergun @uid</c>，不带参数即满级（15 级）。
///
/// 与猜心对局不同，水弹的等级是**落库**的：属性 101/15011 存当前等级、101/15012 存级内经验
/// （运行时探针实测 HouseStorage:GetHotSpringAttr(n) = sid 15000+n）。所以这里直接写等级，
/// 不绕经验换算，避免与客户端 WaterGunLevelPoint 阈值表算出两套结果。
///
/// 满级 = WaterGunRewards.json 的最大等级（当前 15），级内经验写 0。
/// 只改等级与经验，**不碰领奖位图 15013**：满级后仍按正常一键领取，逐级领 1~15 的奖励。
/// </summary>
[CommandInfo("watergun", "Set LoveGun (心动水弹) level; no arg = max level.", "Usage: /watergun [level]  (append @uid; no value = max)", new string[]
{
	"lovegun"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandWaterGun : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		int max = WaterGunHelper.MaxLevel();
		if (max <= 0)
		{
			await arg.SendMsg("watergun: Resources/house/LoveGun/WaterGunRewards.json 缺失或为空，无法取满级等级。");
			return;
		}
		int level = arg.GetInt(0);
		if (level <= 0 || level > max)
		{
			level = max;
		}
		int oldLevel = WaterGunHelper.ReadLevel(player);
		int oldPoint = WaterGunHelper.ReadPoint(player);
		if (oldLevel == level && oldPoint == 0)
		{
			await arg.SendMsg($"watergun: 已是 Lv{oldLevel}/{max}（级内经验 0），无需改动。uid {player.Uid}。");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		await HouseAttr.SetAsync(arg.Target, WaterGunHelper.LevelSid, (uint)level, sync);
		await HouseAttr.SetAsync(arg.Target, WaterGunHelper.PointSid, 0u, sync);
		await player.SendPacket(1004, sync);
		// 1004 之后再来一条 1010 ExtraSync，界面才会立刻重画（同 money / guesspoker 的做法）。
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		await arg.SendMsg($"watergun: Lv{oldLevel}(exp {oldPoint}) -> Lv{level}(exp 0), max Lv{max} uid {player.Uid}. 重开心动水弹界面即可看到；奖励仍走正常领取（可一键领取 1~{level}）。");
	}
}
