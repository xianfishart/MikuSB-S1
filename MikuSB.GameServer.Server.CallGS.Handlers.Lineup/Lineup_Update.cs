using System.Text.Json;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Lineup;

[CallGSApi("Lineup_Update")]
public class Lineup_Update : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		LineupUpdateParam req = JsonSerializer.Deserialize<LineupUpdateParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "UpdateLineup", "{}");
		}
		else if (await connection.Player.LineupManager.UpdateLineup(req.Index, req.Member1, req.Member2, req.Member3, sendPacket: true, req.Name) == null)
		{
			await CallGSRouter.SendScript(connection, "UpdateLineup", "{}");
		}
		else
		{
			await CallGSRouter.SendScript(connection, "UpdateLineup", "{}");
		}
	}
}
