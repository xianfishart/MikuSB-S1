using System;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3DScoreModel_EnterBuffLevel")]
public class Rogue3DScoreModel_EnterBuffLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_EnterBuffLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}");
	}
}
