using System;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerLevel_EnterLevel")]
public class TowerLevel_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		TowerLevelEnterLevelParam req = JsonSerializer.Deserialize<TowerLevelEnterLevelParam>(param);
		if (req == null || req.LevelId == 0 || req.TeamId <= 0)
		{
			await CallGSRouter.SendScript(connection, "TowerLevel_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.TowerLevelData.ContainsKey((uint)req.LevelId))
		{
			await CallGSRouter.SendScript(connection, "TowerLevel_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		string rsp = $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}";
		await CallGSRouter.SendScript(connection, "TowerLevel_EnterLevel", rsp);
	}
}
