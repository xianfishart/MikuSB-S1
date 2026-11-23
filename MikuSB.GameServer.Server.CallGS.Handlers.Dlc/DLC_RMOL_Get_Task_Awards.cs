using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 450 节：繁荣嘉奖 → 绘梦档案（周常任务）领奖。
///
/// 客户端 umg_rmol_reward 的任务格子点「领取」时发
/// {"Tasks":[20601],"Session":1}（早期抓包只带单条时是 {"nId":20601}），
/// 服务端原先没有 handler，点了没反应（按钮一直是灰的 / 点了弹不出去）。
///
/// 回包对应 s2c.tbCmds["DLC_RMOL_Get_Task_Awards"] 触发的
/// EventSystem.Trigger("DLC_RMOL_Get_Task_Awards_Return", tb.Tasks, tb.Awards)：
///   tb.Tasks  —— 这次成功领取的任务 ID 数组（客户端按它把格子标成已领）
///   tb.Awards —— 发放的奖励，五行数组列表 [[类型,?,ID,?,数量], ...]
///
/// 任务定义与进度见 RmolTaskService（属性组 7，bit0 已领 / bit1+ 进度）。
/// </summary>
[CallGSApi("DLC_RMOL_Get_Task_Awards")]
public class DLC_RMOL_Get_Task_Awards : ICallGSHandler
{
	private static readonly Logger Log = new Logger("DLC_RMOL_TaskAward");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		PlayerInstance? player = connection.Player;
		if (player == null)
		{
			await Send(connection, new JsonArray(), new JsonArray());
			return;
		}

		List<int> taskIds = ReadTaskIds(request);
		if (taskIds.Count == 0)
		{
			// 空参数当成「领取所有可领」，避免客户端换了参数键名就静默失败。
			taskIds = RmolTaskService.Claimable(player);
		}

		NtfSyncPlayer sync = new NtfSyncPlayer();
		(List<int> claimed, JsonArray awards, int expGain) = await RmolTaskService.ClaimAsync(player, taskIds, sync);

		JsonArray ids = new JsonArray();
		foreach (int id in claimed)
		{
			ids.Add(id);
		}
		Log.Info($"uid={player.Uid} rmol task award req=[{string.Join(',', taskIds)}] " +
			$"claimed=[{string.Join(',', claimed)}] awards={awards.Count} exp+{expGain}");
		await Send(connection, ids, awards, expGain, sync);
	}

	private static async Task Send(Connection connection, JsonArray tasks, JsonArray awards, int expGain = 0, NtfSyncPlayer? sync = null)
	{
		JsonObject rsp = new JsonObject
		{
			["Tasks"] = tasks,
			["Awards"] = awards,
			// 第 451 节：繁荣嘉奖「缤纷补给」经验。客户端老版本不读这个字段也无妨，
			// 真正的等级推进靠 NtfSyncPlayer 同步属性 (172,100)。
			["ExpGain"] = expGain
		};
		await CallGSRouter.SendScript(connection, "DLC_RMOL_Get_Task_Awards", rsp.ToJsonString(), sync);
	}

	private static List<int> ReadTaskIds(JsonObject request)
	{
		List<int> list = new List<int>();
		foreach (string key in new[] { "Tasks", "TaskIds", "TaskIDs", "Ids", "IDs", "Levels" })
		{
			if (request[key] is not JsonArray array)
			{
				continue;
			}
			foreach (JsonNode? node in array)
			{
				if (node is JsonValue value && value.TryGetValue<double>(out double number) && number > 0)
				{
					list.Add((int)number);
				}
				else if (int.TryParse(node?.ToString(), out int parsed) && parsed > 0)
				{
					list.Add(parsed);
				}
			}
		}
		foreach (string key in new[] { "nId", "TaskId", "TaskID", "Id", "ID" })
		{
			JsonNode? node = request[key];
			if (node is JsonValue value && value.TryGetValue<double>(out double number) && number > 0)
			{
				list.Add((int)number);
			}
			else if (int.TryParse(node?.ToString(), out int parsed) && parsed > 0)
			{
				list.Add(parsed);
			}
		}
		return list;
	}
}
