using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Photo;

[CallGSApi("PSCamera_SetLog")]
public class PSCamera_SetLog : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
