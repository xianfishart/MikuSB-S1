using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_GetQuestAward")]
public class BagRogue_GetQuestAward : CallGSHandler<BagRogueQuestAwardParam>
{
	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueQuestAwardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		BagRogueScheduleExcel schedule = BagRogueService.ScheduleOf(player);
		JsonArray awards = new JsonArray();
		if (schedule == null || request.Ids == null || request.Ids.Count == 0)
		{
			return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = awards });
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int granted = 0;
		foreach (uint taskId in request.Ids)
		{
			if (!schedule.TaskIds.Contains(taskId) || !GameData.ActivityQuestData.TryGetValue(taskId, out ActivityQuestExcel quest))
			{
				continue;
			}
			foreach (JsonNode row in (await AchievementClaimService.TryClaimAsync(player, 7u, taskId, quest.CheckValue, AchievementClaimService.ReadRewardRows(quest.Rewards), sync)) ?? new JsonArray())
			{
				if (!(row is JsonArray cells))
				{
					continue;
				}
				JsonArray item = new JsonArray();
				foreach (JsonNode item2 in cells)
				{
					item.Add(item2?.DeepClone());
				}
				awards.Add(item);
				granted++;
			}
			quest = null;
		}
		if (granted > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = awards }, sync);
	}
}
