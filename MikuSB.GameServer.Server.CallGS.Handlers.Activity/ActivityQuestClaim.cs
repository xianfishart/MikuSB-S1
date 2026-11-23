using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

internal static class ActivityQuestClaim
{
	public static async Task<(JsonArray Flat, JsonArray Groups)> ClaimAsync(PlayerInstance player, JsonArray? idList, NtfSyncPlayer sync)
	{
		JsonArray flat = new JsonArray();
		JsonArray groups = new JsonArray();
		if (idList == null)
		{
			return (Flat: flat, Groups: groups);
		}
		foreach (JsonNode item in idList)
		{
			uint questId = ReadUint(item);
			if (questId == 0 || !GameData.ActivityQuestData.TryGetValue(questId, out ActivityQuestExcel quest))
			{
				continue;
			}
			JsonArray reward = await AchievementClaimService.TryClaimAsync(rewardRows: AchievementClaimService.ReadRewardRows(quest.Rewards), player: player, groupId: 7u, id: questId, checkValue: quest.CheckValue, sync: sync);
			if (reward == null || reward.Count == 0)
			{
				continue;
			}
			JsonArray group = new JsonArray();
			foreach (JsonNode item2 in reward)
			{
				JsonNode clone = item2?.DeepClone();
				flat.Add(clone?.DeepClone());
				group.Add(clone);
			}
			groups.Add(group);
			quest = null;
		}
		return (Flat: flat, Groups: groups);
	}

	public static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
