using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_GetReward")]
public class JigsawPuzzle_GetReward : CallGSHandler<JigsawGetRewardParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_GetReward");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JigsawGetRewardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		JigsawScheduleExcel schedule = JigsawPuzzleService.FindSchedule(request.ActId);
		if (schedule == null)
		{
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		int taskId = request.TaskId;
		bool flag = (uint)(taskId - -1) <= 1u;
		bool quick = flag;
		if (!quick && !schedule.TaskIds.Contains((uint)request.TaskId))
		{
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray awards = new JsonArray();
		foreach (uint taskId2 in schedule.TaskIds)
		{
			if (!quick && taskId2 != (uint)request.TaskId)
			{
				continue;
			}
			ActivityQuestExcel quest = GameData.ActivityQuestData.GetValueOrDefault(taskId2);
			if (quest == null || quest.Rewards == null)
			{
				continue;
			}
			JsonArray granted = await AchievementClaimService.TryClaimAsync(player, 7u, taskId2, quest.CheckValue, AchievementClaimService.ReadRewardRows(quest.Rewards), sync);
			if (granted == null)
			{
				continue;
			}
			foreach (JsonNode item in granted)
			{
				awards.Add(item?.DeepClone());
			}
		}
		if (awards.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		Log.Info($"GetReward: schedule={request.ActId} task={(quick ? (-1) : request.TaskId)} rows={awards.Count} for uid={player.Uid}.");
		return CallGSResult.Ok(new JsonObject { ["tbReward"] = awards }, sync);
	}
}
