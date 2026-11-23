using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("DreamCard_QuickGetReward")]
public class DreamCard_QuickGetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		var (flat, _) = await ActivityQuestClaim.ClaimAsync(player, request["tbIdList"] as JsonArray, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "DreamCard_QuickGetReward", new JsonObject { ["tbAwards"] = flat }.ToJsonString(), sync);
	}
}
