using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("AchievementPoint_GetReward")]
public class AchievementPoint_GetReward : ICallGSHandler
{
	private const uint PointGid = 8u;

	private const uint DailyPointSid = 1u;

	private const uint DailyRewardStartSid = 10u;

	private const uint DailyGroup = 1u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject req = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint group = ReadUint(req["nGroup"]);
		uint index = ReadUint(req["nIndex"]);
		if (group != 1 || index == 0)
		{
			await CallGSRouter.SendScript(connection, "AchievementPoint_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint point = DailyPointService.Recompute(player, sync);
		List<AchievementPointRewardExcel> rows = (from x in GameData.PointRewardData
			where x.Group == @group
			orderby x.Point
			select x).ToList();
		if (index > rows.Count)
		{
			await CallGSRouter.SendScript(connection, "AchievementPoint_GetReward", "{\"sErr\":\"error.BadParam\"}", sync);
			return;
		}
		AchievementPointRewardExcel row = rows[(int)(index - 1)];
		if (point < row.Point)
		{
			await CallGSRouter.SendScript(connection, "AchievementPoint_GetReward", "{\"sErr\":\"tip.point_not_enough\"}", sync);
			return;
		}
		uint sid = 10 + index - 1;
		if (player.Attributes.GetValue(8u, sid) != 0)
		{
			await CallGSRouter.SendScript(connection, "AchievementPoint_GetReward", "{\"sErr\":\"achievement.RewardGeted\"}", sync);
			return;
		}
		List<List<uint>> rewards = AchievementClaimService.ReadRewardRows(row.Rewards);
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rewards, sync);
		PlayerAttr claimedAttr = player.Attributes.GetOrCreate(8u, sid);
		claimedAttr.Val = 1u;
		player.Attributes.SyncTo(sync, claimedAttr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "AchievementPoint_GetReward", new JsonObject { ["tbAward"] = granted }.ToJsonString(), sync);
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
