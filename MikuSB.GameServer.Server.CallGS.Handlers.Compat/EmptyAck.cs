using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

public abstract class EmptyAck : ICallGSHandler
{
	protected abstract string Api { get; }

	protected virtual string Payload => "{}";

	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, Api, Payload);
	}
}
