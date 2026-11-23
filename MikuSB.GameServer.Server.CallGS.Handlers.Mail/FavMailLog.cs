using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Mail;

[CallGSApi("FavMailLog")]
public class FavMailLog : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
