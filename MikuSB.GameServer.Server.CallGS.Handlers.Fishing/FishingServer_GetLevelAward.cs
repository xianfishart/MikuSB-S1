using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

[CallGSApi("FishingServer_GetLevelAward")]
public class FishingServer_GetLevelAward : ICallGSHandler
{
	private const uint Gid = 32u;

	private const uint FlagBase = 10u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint level = ReadUint(request["nLevel"]);
		if (level == 0 || !GameData.FishingLevelData.TryGetValue(level, out FishingLevelExcel config))
		{
			await CallGSRouter.SendScript(connection, "FishingServer_GetLevelAward", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		uint current = FishingRules.GetFishingLevel(player);
		if (level > current)
		{
			await CallGSRouter.SendScript(connection, "FishingServer_GetLevelAward", "{\"sError\":\"tip.star_not_enough\"}");
			return;
		}
		uint flagSid = level / 30 + 10;
		int bit = (int)(level % 30);
		PlayerAttr flag = player.Attributes.GetOrCreate(32u, flagSid);
		if (((flag.Val >> bit) & 1) != 0)
		{
			await CallGSRouter.SendScript(connection, "FishingServer_GetLevelAward", "{\"sError\":\"achievement.RewardGeted\"}");
			return;
		}
		List<List<uint>> rows = RewardText.Parse(config.Award);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		flag.Val |= (uint)(1 << bit);
		player.Attributes.SyncTo(sync, flag);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "FishingServer_GetLevelAward", new JsonObject
		{
			["nLevel"] = level,
			["tbAwardList"] = granted
		}.ToJsonString(), sync);
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
