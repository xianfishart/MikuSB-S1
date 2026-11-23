using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

[CallGSApi("SeasonBoss_GetReward")]
public class SeasonBoss_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		List<uint> taskIds = ReadTaskIds(request["tbTaskID"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		foreach (uint taskId in taskIds)
		{
			if (!SeasonBossQuest.IsSeasonBossTask(taskId) || !GameData.ActivityQuestData.TryGetValue(taskId, out ActivityQuestExcel quest))
			{
				continue;
			}
			JsonArray reward = await AchievementClaimService.TryClaimAsync(rewardRows: AchievementClaimService.ReadRewardRows(quest.Rewards), player: player, groupId: 7u, id: taskId, checkValue: quest.CheckValue, sync: sync);
			if (reward == null)
			{
				continue;
			}
			foreach (JsonNode item in reward)
			{
				granted.Add(item?.DeepClone());
			}
			quest = null;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "SeasonBoss_GetReward", new JsonObject { ["tbAward"] = granted }.ToJsonString(), sync);
	}

	private static List<uint> ReadTaskIds(JsonNode? node)
	{
		List<uint> list = new List<uint>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			uint num = ((item is JsonObject jsonObject) ? ReadUint(jsonObject["nId"]) : ReadUint(item));
			if (num != 0 && !list.Contains(num))
			{
				list.Add(num);
			}
		}
		return list;
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
