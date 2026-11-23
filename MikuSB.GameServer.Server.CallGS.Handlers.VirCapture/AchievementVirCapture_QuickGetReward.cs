using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("AchievementVirCapture_QuickGetReward")]
public class AchievementVirCapture_QuickGetReward : ICallGSHandler
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
				uint missionId = VirCaptureTaskStore.ReadUint(item);
				JsonArray granted = await VirCaptureAchievementClaim.TryClaim(player, missionId, sync);
				if (granted != null && granted.Count > 0)
				{
					groups.Add(granted);
				}
			}
		}
		if (groups.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		await CallGSRouter.SendScript(connection, "AchievementVirCapture_QuickGetReward", new JsonObject { ["tbRewards"] = groups }.ToJsonString(), sync);
	}
}
