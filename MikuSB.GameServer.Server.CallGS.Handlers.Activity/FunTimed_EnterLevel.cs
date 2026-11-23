using System;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("FunTimed_EnterLevel")]
public class FunTimed_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "FunTimed_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}");
	}
}
