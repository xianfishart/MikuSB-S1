using System;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("DefendLogic_EnterLevel")]
public class Compat_DefendLogic_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, "DefendLogic_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}");
	}
}
