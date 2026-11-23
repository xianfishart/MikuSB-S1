using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTask_DoSomething")]
public class VirCaptureTask_DoSomething : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint triggerType = VirCaptureTaskStore.ReadUint(request["nType"]);
		uint missionId = VirCaptureTaskStore.ReadUint(request["nCheckTaskId"]);
		uint mapId = VirCaptureTaskStore.ReadUint(request["nMapId"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject response = new JsonObject();
		// 第 497 节：蓝图岛任务走属性组 163（客户端 VirCaptureTask.GroupId），
		// 表是 build/vircapture_task.json；和「开拓目标」的 133 + vircapture_mission.json 是两套，
		// 两张表的 ID 有 53 个重号（100~115、201~217、301~305…），所以不能只看 ID：
		//   · 蓝图岛的请求一定带 nMapId（326 岛屿 / 5810 副本）；
		//   · 只在 133 表里存在的 ID 仍旧按开拓目标处理。
		// 原来这里完全不分表，蓝图岛的对话/建造/交互事件全写进 133，客户端读 163 永远是 0，
		// 任务既不完成也不推进 —— 这就是「蓝图岛没有任务」的直接原因。
		bool islandRequest = mapId == 326u || mapId == 5810u || !GameData.VirCaptureMissionData.ContainsKey(missionId);
		if (islandRequest && VirCaptureBuildTaskTable.TryGet(missionId, out VirCaptureBuildTaskRow buildTask))
		{
			if (VirCaptureBuildTaskState.Advance(player, buildTask, 1u, sync) > 0)
			{
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
			if (triggerType == 23u)
			{
				response["nTaskId"] = missionId;
			}
			await CallGSRouter.SendScript(connection, "VirCaptureTask_DoSomething", response.ToJsonString(), sync);
			return;
		}
		if (missionId != 0 && GameData.VirCaptureMissionData.TryGetValue(missionId, out VirCaptureMissionExcel mission))
		{
			uint progress = VirCaptureTaskStore.GetProgress(player, missionId);
			if (progress < mission.CheckValue)
			{
				progress++;
				VirCaptureTaskStore.SetProgress(player, missionId, progress, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
			if (triggerType == 23)
			{
				response["nTaskId"] = missionId;
			}
		}
		await CallGSRouter.SendScript(connection, "VirCaptureTask_DoSomething", response.ToJsonString(), sync);
	}
}
