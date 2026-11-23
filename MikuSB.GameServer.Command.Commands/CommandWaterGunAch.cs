using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 心动水弹（LoveGun）全部成就解锁（2026-11-12 新增，修复记录第 264 节）。
/// 网页面板「心动水弹解锁全部成就」按钮调 <c>/watergunach @uid</c>。
///
/// 与「一键满级」不同，这条命令不碰等级（101/15011），只写成就进度：
/// 成就 = Resources/achievement/activity_quest.json 里 ID 20101~20120、Type 264~276 共 20 条，
/// 进度落在 Gid=7 / Sid=成就ID，Val = (进度 &lt;&lt; 1) | 已领标记。
/// 这里把每条的目标进度（CheckValue，含 5000 / 10000 / 20000 这类累计项）一次写满，
/// 并保留最低位的「已领」标记不动 —— 只解锁，不发奖励，奖励仍走界面正常「一键领取」
/// （第 261 / 263 节的领取链路）。
/// </summary>
[CommandInfo("watergunach", "Unlock all LoveGun (心动水弹) achievements; rewards stay claimable in game.", "Usage: /watergunach  (append @uid)", new string[]
{
	"lovegunach",
	"watergunachieve"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandWaterGunAch : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		List<KeyValuePair<int, int>> rows = WaterGunHelper.LoadAchRows();
		if (rows.Count == 0)
		{
			await arg.SendMsg("watergunach: Resources/achievement/activity_quest.json 里找不到水弹成就（20101~20120 / Type 264~276）。");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		var (total, newly, already) = WaterGunHelper.UnlockAllAch(player, sync);
		await player.SendPacket(1004, sync);
		// 1004 之后再来一条 1010 ExtraSync，界面才会立刻重画（同 watergun / guesspoker 的做法）。
		if (arg.Target is Connection connection)
		{
			await CallGSRouter.SendScript(connection, "", "{}", sync);
		}
		await arg.SendMsg($"watergunach: 心动水弹成就 {total} 条已全部达成（本次解锁 {newly} 条，原本已达成 {already} 条），已存档。奖励不会自动发，请打开活动界面点「一键领取」正常领取。uid {player.Uid}。");
	}
}
