using System;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 手工打开一个活动窗口（第 423 节）。
///
/// 背景：DLC 期数窗口（第 33 期等）里「适格启忆录 / 拼图」这类活动卡片的开锁状态
/// 取自属性组 50 的开放时间（sid = activityId * 10）。活动自己的 Excel 窗口是
/// 历史期（如 390 = 2026-04-02），不在本期 era 窗口内，登录时的
/// ActivityWindowGuard.Push 会把它还原成历史开放时间，客户端于是判成「已结束/锁」。
///
/// 用法：/actopen &lt;活动 id&gt; [天数] [@uid]
///   /actopen 390        按 30 天开
/// </summary>
[CommandInfo("actopen", "Force-open one activity window (attribute group 50).", "Usage: /actopen <activityId> [days]  (append @uid)", new string[]
{
	"aop"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandActOpen : ICommands
{
	private const uint GroupId = 50u;

	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		int activityId = arg.GetInt(0);
		int days = arg.GetInt(1);
		if (activityId <= 0)
		{
			await arg.SendMsg("Usage: /actopen <活动id> [天数]  (append @uid)");
			return;
		}
		if (days <= 0)
		{
			days = 30;
		}
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		long now = DateTimeOffset.Now.ToUnixTimeSeconds();
		long start = now - 86400;
		long end = now + (long)days * 86400;
		uint sid = (uint)activityId * 10u;
		PlayerAttr attr = player.Attributes.GetOrCreate(GroupId, sid);
		uint old = attr.Val;
		attr.Val = (uint)start;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(GroupId, sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(GroupId, sid)] = attr.Val;
		var json = new System.Text.Json.Nodes.JsonObject
		{
			["nId"] = activityId,
			["nStartTime"] = start,
			["nEndTime"] = end,
			["nCloseFlag"] = 0
		};
		await CallGSRouter.SendScript(arg.Target, "ActivityQuest_Open", json.ToJsonString(), sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await arg.SendMsg($"actopen: 活动 {activityId} 窗口 {start}~{end}（原 {old}），已下发 ActivityQuest_Open。uid {player.Uid}。");
	}
}
