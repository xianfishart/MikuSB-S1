using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Photo;

[CallGSApi("PSCamera_SetEnterLog")]
public class PSCamera_SetEnterLog : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
