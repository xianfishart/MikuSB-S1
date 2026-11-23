using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

[CallGSApi("DonoDuelTask_GetReward")]
public class DonoDuelTask_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint missionId = DonoDuelTaskStore.ReadUint(request["nId"]);
		if (missionId == 0 || !GameData.DonoDuelTaskData.TryGetValue(missionId, out DonoDuelTaskExcel config))
		{
			await CallGSRouter.SendScript(connection, "DonoDuelTask_GetReward", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		if (DonoDuelTaskStore.IsClaimed(player, missionId))
		{
			await CallGSRouter.SendScript(connection, "DonoDuelTask_GetReward", "{\"sError\":\"achievement.RewardGeted\"}");
			return;
		}
		if (DonoDuelTaskStore.Progress(player, missionId) < config.CheckValue)
		{
			await CallGSRouter.SendScript(connection, "DonoDuelTask_GetReward", "{\"sError\":\"achievement.NotFinished\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<List<uint>> rows = RewardText.Parse(config.Reward1);
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		int cards = DonoDuelCards.GrantForTask(player, missionId, config.Reward2, sync);
		// 第 483 节（追加）：支线任务的卡奖励只在 Reward3 里（带星级/品质），
		// Reward2 为空时回落到 Reward3；101 的初始卡组在接取时已由 StarterDeck 发过，跳过。
		if (cards == 0 && missionId != DonoDuelCards.StarterMissionId)
		{
			DonoDuelCards.GrantReward3(player, config.Reward3, sync);
		}
		DonoDuelTaskStore.MarkClaimed(player, missionId, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "DonoDuelTask_GetReward", new JsonObject
		{
			["nId"] = missionId,
			["tbRewards"] = new JsonObject { ["tbItems"] = granted }
		}.ToJsonString(), sync);
	}
}
