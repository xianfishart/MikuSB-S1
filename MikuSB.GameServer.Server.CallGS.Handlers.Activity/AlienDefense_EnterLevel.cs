using System;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("AlienDefense_EnterLevel")]
public class AlienDefense_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "AlienDefense_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}");
	}
}
