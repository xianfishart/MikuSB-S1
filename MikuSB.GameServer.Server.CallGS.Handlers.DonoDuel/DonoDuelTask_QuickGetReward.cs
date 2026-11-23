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

[CallGSApi("DonoDuelTask_QuickGetReward")]
public class DonoDuelTask_QuickGetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray groups = new JsonArray();
		JsonNode jsonNode = request["tbIdList"];
		if (jsonNode is JsonArray list)
		{
			foreach (JsonNode item in list)
			{
				uint missionId = DonoDuelTaskStore.ReadUint(item);
				if (missionId != 0 && GameData.DonoDuelTaskData.TryGetValue(missionId, out DonoDuelTaskExcel config) && !DonoDuelTaskStore.IsClaimed(player, missionId) && DonoDuelTaskStore.Progress(player, missionId) >= config.CheckValue)
				{
					List<List<uint>> rows = RewardText.Parse(config.Reward1);
					JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
					int cards = DonoDuelCards.GrantForTask(player, missionId, config.Reward2, sync);
					// 第 483 节（追加）：支线任务的卡奖励只在 Reward3 里，Reward2 空时回落。
					if (cards == 0 && missionId != DonoDuelCards.StarterMissionId)
					{
						DonoDuelCards.GrantReward3(player, config.Reward3, sync);
					}
					DonoDuelTaskStore.MarkClaimed(player, missionId, sync);
					groups.Add(granted);
					config = null;
				}
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "DonoDuelTask_QuickGetReward", new JsonObject { ["tbRewards"] = groups }.ToJsonString(), sync);
	}
}
