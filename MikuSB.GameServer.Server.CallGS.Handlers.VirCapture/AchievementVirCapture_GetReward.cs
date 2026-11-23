using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("AchievementVirCapture_GetReward")]
public class AchievementVirCapture_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint missionId = VirCaptureTaskStore.ReadUint(request["nId"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = await VirCaptureAchievementClaim.TryClaim(player, missionId, sync);
		if (granted == null)
		{
			await CallGSRouter.SendScript(connection, "AchievementVirCapture_GetReward", "{\"sError\":\"achievement.NotFinished\"}");
			return;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "AchievementVirCapture_GetReward", new JsonObject
		{
			["nId"] = missionId,
			["tbRewards"] = new JsonArray { granted }
		}.ToJsonString(), sync);
	}
}
