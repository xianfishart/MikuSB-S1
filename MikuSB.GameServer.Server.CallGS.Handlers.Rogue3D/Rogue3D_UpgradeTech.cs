using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue3D;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_UpgradeTech")]
public class Rogue3D_UpgradeTech : ICallGSHandler
{
	private const uint GroupId = 124u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		uint techId = 0u;
		try
		{
			techId = JsonSerializer.Deserialize<UpgradeTechParam>(param)?.TechId ?? 0;
		}
		catch
		{
		}
		if (techId == 0)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_UpgradeTech", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!Rogue3DUpgradeService.TryUpgrade(player, techId, sync, out string error, out uint newLevel, out uint maxLevel, out uint spendType, out uint spendAmount))
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_UpgradeTech", "{\"sErr\":\"" + error + "\"}");
			return;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject response = new JsonObject
		{
			["nTechId"] = techId,
			["nLevel"] = newLevel,
			["nMaxLevel"] = maxLevel
		};
		await CallGSRouter.SendScript(connection, "Rogue3D_UpgradeTech", response.ToJsonString(), sync);
	}
}
