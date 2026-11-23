using System;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("TowerEventChapter_EnterLevel")]
public class TowerEventChapter_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		TowerEventEnterLevelParam req = JsonSerializer.Deserialize<TowerEventEnterLevelParam>(param);
		if (req == null || req.LevelId == 0 || req.TeamId <= 0)
		{
			await CallGSRouter.SendScript(connection, "TowerEventChapter_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.TowerEventLevelData.ContainsKey((uint)req.LevelId))
		{
			await CallGSRouter.SendScript(connection, "TowerEventChapter_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		string rsp = $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}";
		await CallGSRouter.SendScript(connection, "TowerEventChapter_EnterLevel", rsp);
	}
}
