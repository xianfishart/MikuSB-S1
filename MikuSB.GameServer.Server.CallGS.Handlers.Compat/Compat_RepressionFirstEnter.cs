using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("Repression_Chapter_FirstEnter")]
public class Compat_RepressionFirstEnter : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
