using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Lineup;

[CallGSApi("Lineups_Update")]
public class Lineups_Update : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		List<LineupUpdateBatchParam> req = JsonSerializer.Deserialize<List<LineupUpdateBatchParam>>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "UpdateLineup", "{}");
			return;
		}
		foreach (LineupUpdateBatchParam lineup in req)
		{
			if (lineup != null)
			{
				await connection.Player.LineupManager.UpdateLineup(lineup.Index, lineup.Member1, lineup.Member2, lineup.Member3);
			}
		}
		DatabaseHelper.SaveDatabaseType(connection.Player.LineupManager.LineupData);
		await CallGSRouter.SendScript(connection, "UpdateLineup", "{}");
	}
}
