using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Extra_Mission_GetTaskAward")]
public class Extra_Mission_GetTaskAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint activityId = ExtraMissionStore.ReadUint(request["nActivityId"]);
		int taskIdx = (int)ExtraMissionStore.ReadUint(request["nTaskIdx"]);
		JsonObject response = new JsonObject
		{
			["nActivityId"] = activityId,
			["nTaskIdx"] = taskIdx,
			["sAPI"] = "Extra_Mission_GetTaskAward",
			["tbAwards"] = new JsonArray()
		};
		if (activityId == 0 || taskIdx < 1 || taskIdx > 7)
		{
			response["sErr"] = "error.BadParam";
			await CallGSRouter.SendScript(connection, "Extra_Mission_GetTaskAward", response.ToJsonString());
			return;
		}
		uint questId = player.GetAttr(50u, ExtraMissionStore.DiySid(activityId, taskIdx));
		if (questId == 0 || !GameData.ActivityQuestData.TryGetValue(questId, out ActivityQuestExcel quest))
		{
			response["sErr"] = "ui.TxtNotOpen";
			await CallGSRouter.SendScript(connection, "Extra_Mission_GetTaskAward", response.ToJsonString());
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = await AchievementClaimService.TryClaimAsync(rewardRows: AchievementClaimService.ReadRewardRows(quest.Rewards), player: player, groupId: 7u, id: questId, checkValue: quest.CheckValue, sync: sync);
		if (granted == null)
		{
			response["sErr"] = "achievement.NotFinished";
			await CallGSRouter.SendScript(connection, "Extra_Mission_GetTaskAward", response.ToJsonString(), sync);
			return;
		}
		ExtraMissionStore.SetFlag(player, activityId, 16 + taskIdx - 1, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		response["tbAwards"] = granted;
		await CallGSRouter.SendScript(connection, "Extra_Mission_GetTaskAward", response.ToJsonString(), sync);
	}
}
