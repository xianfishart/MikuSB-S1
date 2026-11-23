using System;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 「蓝图币」发放（2026-11-22 新增，修复记录第 515 节）。默认 50 万。
///
/// 蓝图币 = 新星开拓的岛内现金（cashId 55 = operating_data.json 的 Cashid(326)），
/// 余额落在属性 (1, 111)，建筑升级 / 属性升级 / 员工培训 / 招聘都扣它。
/// 发完顺手刷一遍开拓目标进度（「累计获得蓝图币」按余额差值追踪，第 513 节），
/// 这样职业精算那一档也能立刻跟上。
/// </summary>
[CommandInfo("bluecoin", "Give 蓝图币 (VirCapture cash 55, attr 1/111) to target.", "Usage: /bluecoin [amount=500000]  (append @uid)", new string[]
{
	"蓝图币",
	"lan_tu_bi",
	"vccash"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandBlueCoin : ICommands
{
	public const uint AttrGroup = 1u;

	/// <summary>蓝图币余额的属性 Id（1/111，第 509 节确认）。</summary>
	public const uint AttrId = 111u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		long amount = arg.GetInt(0);
		if (amount <= 0)
		{
			amount = 500000;
		}
		if (amount > 100000000)
		{
			amount = 100000000;
		}
		PlayerInstance player = arg.Target.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.GetOrCreate(AttrGroup, AttrId);
		long next = Math.Min((long)attr.Val + amount, int.MaxValue);
		attr.Val = (uint)next;
		player.Attributes.SyncTo(sync, attr);
		// 经营目标「累计获得蓝图币」按余额差值记账，发完立刻刷新。
		VirCaptureMissionProgress.SyncAll(player, sync);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"bluecoin: 蓝图币 +{amount}，现余额 {attr.Val}（1/111，uid {player.Uid}）。");
	}
}
