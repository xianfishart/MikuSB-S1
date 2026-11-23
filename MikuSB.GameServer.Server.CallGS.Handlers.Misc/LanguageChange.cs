using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("LanguageChange")]
public class LanguageChange : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
