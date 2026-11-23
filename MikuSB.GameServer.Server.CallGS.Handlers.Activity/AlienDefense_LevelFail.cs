using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("AlienDefense_LevelFail")]
public class AlienDefense_LevelFail : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
