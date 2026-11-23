using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTask_GetReward")]
public class VirCaptureTask_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint missionId = VirCaptureTaskStore.ReadUint(request["nId"]);
		// 第 497 节：蓝图岛任务（163 + build/vircapture_task.json）单独走一条，
		// 原来所有任务都按 133 的表校验，蓝图岛任务点领取必然报「未完成」或「参数错误」。
		if (VirCaptureBuildTaskTable.TryGet(missionId, out VirCaptureBuildTaskRow buildTask))
		{
			if (VirCaptureBuildTaskState.IsClaimed(player, missionId))
			{
				await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", "{\"sError\":\"achievement.RewardGeted\"}");
				return;
			}
			if (VirCaptureBuildTaskState.GetProgress(player, missionId) < buildTask.CheckValue)
			{
				await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", "{\"sError\":\"achievement.NotFinished\"}");
				return;
			}
			NtfSyncPlayer buildSync = new NtfSyncPlayer();
			JsonArray buildGranted = await VirCaptureBuildTaskState.GrantAsync(player, buildTask, buildSync);
			VirCaptureBuildTaskState.MarkClaimed(player, missionId, buildSync);
			// 第 511 节：119/227/325 这些剧情任务领奖后，以它为触发点的 Type 7「送达型」任务
			// （122/230/328 送员工）立刻代领，奖励并进同一条回包 —— 客户端只有在 tbRewards 里
			// 看到 nType=4 才会弹「员工已到岗」那张卡，官服过完剧情就是这个表现。
			JsonArray triggerGranted = await VirCaptureBuildTaskState.ClaimTriggerTasksAsync(player, missionId, buildSync);
			foreach (JsonNode? node in triggerGranted)
			{
				buildGranted.Add(node?.DeepClone());
			}
			// 第 504 节：领完子任务（如 108 海滩别墅 4 级）立刻重算合集 107 的进度，
			// 否则 HUD 上「岛屿升级条件」永远是 1/3，评级嘉奖领不了。
			VirCaptureBuildTaskState.AdvanceByBuildLevel(player, buildSync);
			VirCaptureBuildTaskState.AdvanceComposite(player, buildSync);
			// 第 507 节：领奖后前置才算「已领」，Type 19（经营类）/Type 27（指标类）这时才能继续往下推。
			VirCaptureBuildTaskState.SyncDerived(player, buildSync);
			// 第 513 节：领奖可能入账蓝图币，立刻重算「累计获得蓝图币」等经营目标进度。
			VirCaptureMissionProgress.SyncAll(player, buildSync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", new JsonObject
			{
				["nId"] = missionId,
				["tbRewards"] = buildGranted
			}.ToJsonString(), buildSync);
			return;
		}
		if (missionId == 0 || !GameData.VirCaptureMissionData.TryGetValue(missionId, out VirCaptureMissionExcel mission))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		if (VirCaptureTaskStore.IsClaimed(player, missionId))
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", "{\"sError\":\"achievement.RewardGeted\"}");
			return;
		}
		if (VirCaptureTaskStore.GetProgress(player, missionId) < mission.CheckValue)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", "{\"sError\":\"achievement.NotFinished\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<List<uint>> rows = RewardText.Parse(mission.Rewards);
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		VirCaptureTaskStore.MarkClaimed(player, missionId, sync);
		// 第 513 节：领奖可能入账蓝图币，立刻重算「累计获得蓝图币」等经营目标进度。
		VirCaptureMissionProgress.SyncAll(player, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "VirCaptureTask_GetReward", new JsonObject
		{
			["nId"] = missionId,
			["tbRewards"] = granted
		}.ToJsonString(), sync);
	}
}
