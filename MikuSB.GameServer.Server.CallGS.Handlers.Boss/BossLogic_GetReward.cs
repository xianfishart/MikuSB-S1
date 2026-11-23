using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Boss;

[CallGSApi("BossLogic_GetReward")]
public class BossLogic_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint mileage = BossStore.ReadUint(request["Mileage"]);
		BossStore.BossAwardRow row = BossStore.Awards().FirstOrDefault((BossStore.BossAwardRow x) => x.Id == mileage);
		if (row == null)
		{
			await CallGSRouter.SendScript(connection, "BossLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (BossStore.IsMileageReceived(player, row.Id))
		{
			await CallGSRouter.SendScript(connection, "BossLogic_GetReward", "{\"sErr\":\"achievement.RewardGeted\"}", sync);
			return;
		}
		uint total = BossStore.TotalIntegral(player);
		if (total < row.ScoreCount)
		{
			await CallGSRouter.SendScript(connection, "BossLogic_GetReward", "{\"sErr\":\"ui.TxtNotOpen\"}", sync);
			return;
		}
		List<List<uint>> rewards = AchievementClaimService.ReadRewardRows(row.ScoreAward);
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rewards, sync);
		BossStore.MarkMileageReceived(player, row.Id, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "BossLogic_GetReward", new JsonObject { ["tbAward"] = granted }.ToJsonString(), sync);
	}
}
