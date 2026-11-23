using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Extra_Mission_GetSumAward")]
public class Extra_Mission_GetSumAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint activityId = ExtraMissionStore.ReadUint(request["nActivityId"]);
		JsonObject response = new JsonObject
		{
			["nActivityId"] = activityId,
			["sAPI"] = "Extra_Mission_GetSumAward",
			["tbAwards"] = new JsonArray()
		};
		if (activityId == 0)
		{
			response["sErr"] = "error.BadParam";
			await CallGSRouter.SendScript(connection, "Extra_Mission_GetSumAward", response.ToJsonString());
			return;
		}
		if (ExtraMissionStore.HasFlag(player, activityId, 31))
		{
			response["sErr"] = "achievement.RewardGeted";
			await CallGSRouter.SendScript(connection, "Extra_Mission_GetSumAward", response.ToJsonString());
			return;
		}
		for (int idx = 1; idx <= 7; idx++)
		{
			if (!ExtraMissionStore.HasFlag(player, activityId, idx - 1))
			{
				response["sErr"] = "achievement.NotFinished";
				await CallGSRouter.SendScript(connection, "Extra_Mission_GetSumAward", response.ToJsonString());
				return;
			}
			uint questId = player.GetAttr(50u, ExtraMissionStore.DiySid(activityId, idx));
			if (questId == 0 || !GameData.ActivityQuestData.TryGetValue(questId, out ActivityQuestExcel quest) || !AchievementClaimService.IsFinished(player, 7u, questId, quest.CheckValue))
			{
				response["sErr"] = "achievement.NotFinished";
				await CallGSRouter.SendScript(connection, "Extra_Mission_GetSumAward", response.ToJsonString());
				return;
			}
			quest = null;
		}
		JsonObject activity = ExtraMissionStore.Activity(activityId);
		List<List<uint>> rows = new List<List<uint>>();
		JsonNode jsonNode = activity?["CustomData"];
		JsonArray sumRows = default;
		int num;
		if (jsonNode is JsonArray { Count: >7 } custom)
		{
			jsonNode = custom[7];
			sumRows = jsonNode as JsonArray;
			num = ((sumRows != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			foreach (JsonNode node in sumRows)
			{
				JsonArray arr = node as JsonArray;
				if (arr == null || arr.Count < 5)
				{
					continue;
				}
				List<uint> values = new List<uint>(5);
				foreach (JsonNode item in arr.Take(5))
				{
					values.Add(ExtraMissionStore.ReadUint(item));
				}
				if (values.Count == 5 && values[4] != 0)
				{
					rows.Add(values);
				}
			}
		}
		NtfSyncPlayer sync2 = new NtfSyncPlayer();
		JsonArray granted2 = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync2);
		ExtraMissionStore.SetFlag(player, activityId, 31, sync2);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		response["tbAwards"] = granted2;
		await CallGSRouter.SendScript(connection, "Extra_Mission_GetSumAward", response.ToJsonString(), sync2);
	}
}
