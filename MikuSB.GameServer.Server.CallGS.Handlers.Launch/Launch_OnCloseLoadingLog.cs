using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Launch;

[CallGSApi("Launch_OnCloseLoadingLog")]
public class Launch_OnCloseLoadingLog : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
