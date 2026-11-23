using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

[CallGSApi("FishingServer_EndFishing")]
public class FishingServer_EndFishing : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint state = ReadUint(request["nState"]);
		if (!FishingServer_BeginFishing.Pending.TryRemove(player.Uid, out var pending) || state != 1)
		{
			await CallGSRouter.SendScript(connection, "FishingServer_EndFishing", "{}");
			return;
		}
		if (!GameData.FishingFishData.TryGetValue(pending.FishId, out FishingFishExcel fish))
		{
			await CallGSRouter.SendScript(connection, "FishingServer_EndFishing", "{}");
			return;
		}
		uint level = FishingRules.GetFishingLevel(player);
		uint length = FishingRules.Roll(fish.Length, level);
		uint weight = FishingRules.Roll(fish.Weight, level);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint caught = FishingRules.UpdateBook(player, fish, length, weight, sync);
		JsonArray drops = new JsonArray();
		foreach (List<uint> row in FishingRules.MeatRows(fish))
		{
			foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(new List<IReadOnlyList<uint>> { row }, sync))
			{
				drops.Add(item?.DeepClone());
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		JsonObject fishInfo = new JsonObject
		{
			["nID"] = fish.Id,
			["nLength"] = length,
			["nWeight"] = weight,
			["nCaughtNum"] = caught,
			["tbDrops"] = drops
		};
		await CallGSRouter.SendScript(connection, "FishingServer_EndFishing", new JsonObject { ["tbFishInfo"] = fishInfo }.ToJsonString(), sync);
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
