using System;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Repression_Chapter_EnterLevel")]
public class Repression_Chapter_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Repression_Chapter_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}");
	}
}
