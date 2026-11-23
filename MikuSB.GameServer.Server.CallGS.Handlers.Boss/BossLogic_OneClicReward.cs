using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Boss;

[CallGSApi("BossLogic_OneClicReward")]
public class BossLogic_OneClicReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		uint total = BossStore.TotalIntegral(player);
		foreach (BossStore.BossAwardRow row in from x in BossStore.Awards()
			orderby x.Id
			select x)
		{
			List<List<uint>> rewards = AchievementClaimService.ReadRewardRows(row.ScoreAward);
			if (rewards.Count == 0 || BossStore.IsMileageReceived(player, row.Id) || total < row.ScoreCount)
			{
				continue;
			}
			JsonArray rows = await player.RewardManager.GrantConfiguredRewardsAsync(rewards, sync);
			BossStore.MarkMileageReceived(player, row.Id, sync);
			foreach (JsonNode item in rows)
			{
				granted.Add(item?.DeepClone());
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "BossLogic_OneClicReward", new JsonObject { ["tbAward"] = granted }.ToJsonString(), sync);
	}
}
