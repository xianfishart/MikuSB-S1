using System;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_EnterLevel")]
public class Rogue3D_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		int seed = Random.Next(1, 1000000000);
		await CallGSRouter.SendScript(connection, "Rogue3D_EnterLevel", $"{{\"nSeed\":{seed}}}");
	}
}
