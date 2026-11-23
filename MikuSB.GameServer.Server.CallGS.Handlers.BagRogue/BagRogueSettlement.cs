using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public static class BagRogueSettlement
{
	private static readonly Logger Log = new Logger("BagRogueSettlement");

	public static async Task<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(PlayerInstance player, JsonNode? tbParam)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint stageId = BagRogueService.ReadUint(tbParam?["nID"]);
		uint score = BagRogueService.ReadUint(tbParam?["nScore"]);
		uint time = BagRogueService.ReadUint(tbParam?["nTime"]);
		BagRogueNodeExcel node = BagRogueService.CurrentNode(player);
		if (node == null)
		{
			Log.Warn($"BagRogue settlement for stage {stageId} without a walked node (uid={player.Uid}).");
			return (Payload: new JsonObject { ["tbAward"] = new JsonArray() }, Sync: sync);
		}
		if (BagRogueService.IsEndingNode(node))
		{
			List<List<uint>> endingRows = BagRogueService.NodeRewardRows(node);
			if (endingRows.Count > 0)
			{
				await player.RewardManager.GrantConfiguredRewardsAsync(endingRows, sync);
			}
			BagRogueService.AccumulateRewards(player, endingRows, sync);
			BagRogueService.RecordEnding(player, BagRogueService.EndingTypeFor(node), score, time, sync);
			JsonArray endingAwards = BagRogueService.FinishRoute(player, sync);
			BagRogueService.RecordClear(player, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			Log.Info($"BagRogue ending {BagRogueService.EndingTypeFor(node)} at node {node.NodeId} (map {node.MapId}, type {node.EventType}), {endingRows.Count} reward row(s), score={score}, time={time} for uid={player.Uid}.");
			return (Payload: new JsonObject { ["tbAward"] = endingAwards }, Sync: sync);
		}
		JsonArray awards = new JsonArray();
		foreach (List<uint> row in BagRogueService.NodeRewardRows(node))
		{
			JsonArray item = new JsonArray();
			foreach (uint cell in row)
			{
				item.Add(cell);
			}
			awards.Add(item);
		}
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>>();
		foreach (List<uint> row2 in BagRogueService.NodeRewardRows(node))
		{
			rows.Add(row2);
		}
		if (rows.Count > 0)
		{
			await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		}
		BagRogueService.AccumulateRewards(player, rows, sync);
		JsonObject payload = new JsonObject { ["tbAward"] = awards };
		if (BagRogueService.TrySetupRelicPick(player, sync, out List<uint> picks))
		{
			JsonArray pickArray = new JsonArray();
			foreach (uint p in picks)
			{
				pickArray.Add(p);
			}
			payload["tbRelicChoose"] = pickArray;
		}
		else
		{
			BagRogueService.MarkCurrentNodeDone(player, sync);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		Log.Info($"BagRogue stage {stageId} cleared at node {node.NodeId} (map {node.MapId}), {rows.Count} reward row(s) for uid={player.Uid}.");
		return (Payload: payload, Sync: sync);
	}
}
