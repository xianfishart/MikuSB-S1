using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_NodeFinish")]
public class BagRogue_NodeFinish : CallGSHandler
{
	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray awards = BagRogueService.FinishRoute(player, sync);
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>>();
		foreach (JsonNode item in awards)
		{
			if (!(item is JsonArray row))
			{
				continue;
			}
			List<uint> values = new List<uint>();
			foreach (JsonNode item2 in row)
			{
				values.Add(uint.TryParse(item2?.ToString(), out var parsed) ? parsed : 0u);
			}
			if (values.Count >= 5 && values[4] != 0)
			{
				rows.Add(values);
			}
		}
		if (rows.Count > 0)
		{
			await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		return CallGSResult.Ok(new JsonObject { ["tbAward"] = awards }, sync);
	}
}
