using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Settlement;

public abstract class GenericLevelFailHandler : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
