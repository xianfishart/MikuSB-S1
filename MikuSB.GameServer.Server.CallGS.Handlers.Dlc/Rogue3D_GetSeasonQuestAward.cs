using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3D_GetSeasonQuestAward")]
public class Rogue3D_GetSeasonQuestAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonArray rewards = new JsonArray();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonArray ids = new JsonArray();
			foreach (int id in Rogue3D_GetQuestAward.ReadIdList(param))
			{
				ids.Add(id);
			}
			var (flat, _) = await ActivityQuestClaim.ClaimAsync(player, ids, sync);
			foreach (JsonNode item in flat)
			{
				rewards.Add(item?.DeepClone());
			}
			TaskProgressService.Save(player);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		await CallGSRouter.SendScript(connection, "Rogue3D_GetSeasonQuestAward", new JsonObject { ["tbAwardList"] = rewards }.ToJsonString(), sync);
	}
}
