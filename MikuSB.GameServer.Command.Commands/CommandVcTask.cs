using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
/// 新星开拓「开拓目标」进度工具（第 343 节新增）。
///
/// 客户端的状态完全来自属性 (133, MissionID)：bit1..31 = 进度，bit0 = 已领取，
/// 所以进度必须由服务端写。日常进度靠各玩法事件累加（劝降 / 升阶 / 解雇 / 通关），
/// 但钓鱼、蓝图岛、试炼这些系统本服还没有事件源，进度会一直是 0，
/// 这个命令用来查状态、单条补进度，或者一次性把全部任务顶到可领取。
///
/// 用法：
///   /vctask              看当前计数器和任务完成情况
///   /vctask all          所有任务进度 = CheckValue（全部变成可领取）
///   /vctask &lt;id&gt;         指定任务进度 = CheckValue
///   /vctask &lt;id&gt; &lt;val&gt;   指定任务进度 = val
///   /vctask counter &lt;key&gt; &lt;val&gt;  直接改计数器后刷新（key 见 VirCaptureMissionProgress）
/// </summary>
[CommandInfo("vctask", "VirCapture mission progress tool: show / complete one / complete all.", "Usage: /vctask [all | <missionId> [value] | counter <key> <value>]", new string[]
{
	"vctasks",
	"missionprog"
}, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandVcTask : ICommands
{
	[CommandDefault]
	public async ValueTask Run(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		PlayerInstance player = arg.Target.Player;
		if (GameData.VirCaptureMissionData.Count == 0)
		{
			await arg.SendMsg("vctask: vircapture_mission.json 没加载到，先检查 Resources/dlc/vircapture/。");
			return;
		}
		string sub = ((arg.Args.Count > 0) ? arg.Args[0] : "").Trim().ToLowerInvariant();
		NtfSyncPlayer sync = new NtfSyncPlayer();

		if (sub.Length == 0)
		{
			VirCaptureMissionCounterData counters = VirCaptureMissionCounterStore.Get((int)player.Uid);
			StringBuilder sb = new StringBuilder();
			sb.Append("vctask: 计数器 ");
			if (counters.Counters.Count == 0)
			{
				sb.Append("(空)");
			}
			foreach (var kv in counters.Counters.OrderBy(k => k.Key))
			{
				sb.Append($"{kv.Key}={kv.Value} ");
			}
			sb.Append($"| 已通关关卡 {counters.ClearedLevels.Count} 个");
			int can = 0;
			int got = 0;
			foreach (VirCaptureMissionExcel m in GameData.VirCaptureMissionData.Values)
			{
				uint attr = player.Attributes.GetValue(VirCaptureMissionProgress.Gid, m.MissionId);
				if ((attr & 1u) != 0)
				{
					got++;
				}
				else if (((attr >> 1) & 0x7FFFFFFFu) >= m.CheckValue)
				{
					can++;
				}
			}
			sb.Append($"| 任务 {GameData.VirCaptureMissionData.Count} 条：可领取 {can}，已领取 {got}");
			await arg.SendMsg(sb.ToString());
			return;
		}

		if (sub == "all")
		{
			int changed = VirCaptureMissionProgress.CompleteAll(player, sync);
			await player.SendPacket(1004, sync);
			await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			await arg.SendMsg($"vctask: 已把全部任务顶到可领取，改动 {changed} 条，回游戏重新打开「开拓目标」即可领取。");
			return;
		}

		if (sub == "counter")
		{
			string key = (arg.Args.Count > 1) ? arg.Args[1].Trim() : "";
			int counterValue = arg.GetInt(2);
			if (string.IsNullOrWhiteSpace(key))
			{
				await arg.SendMsg("vctask: 用法 /vctask counter <key> <value>，key 例如 gacha / skill_up / dismiss / kill / login_day / fish。");
				return;
			}
			VirCaptureMissionCounterStore.RaiseTo((int)player.Uid, key, counterValue);
			VirCaptureMissionCounterStore.Save((int)player.Uid);
			int changed = VirCaptureMissionProgress.SyncAll(player, sync);
			await player.SendPacket(1004, sync);
			await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			await arg.SendMsg($"vctask: 计数器 {key} = {counterValue}，刷新 {changed} 条任务。");
			return;
		}

		uint missionId = (uint)Math.Max(0, arg.GetInt(0));
		if (missionId == 0 || !GameData.VirCaptureMissionData.TryGetValue(missionId, out VirCaptureMissionExcel mission))
		{
			await arg.SendMsg($"vctask: 没有 Id={missionId} 的任务。用 /vctask 看计数器，或直接 /vctask all。");
			return;
		}
		int raw = arg.GetInt(1);
		uint? value = (raw > 0) ? (uint)raw : (uint?)null;
		bool ok = VirCaptureMissionProgress.CompleteOne(player, missionId, value, sync);
		await player.SendPacket(1004, sync);
		await CallGSRouter.SendScript(arg.Target, "", "{}", sync);
		await arg.SendMsg(ok
			? $"vctask: 任务 {missionId}（{mission.Type}，需要 {mission.CheckValue}）进度已设为 {value ?? mission.CheckValue}。"
			: $"vctask: 任务 {missionId} 进度没变化（可能已经是该值）。");
	}
}
