using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

[CallGSApi("WorldBossLogic_Record")]
public class WorldBossLogic_Record : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "WorldBossLogic_Record", new JsonObject { ["bRecord"] = true }.ToJsonString());
	}
}
