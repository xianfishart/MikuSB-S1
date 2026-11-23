using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("DLC_Chapter_LevelFail")]
public class DLC_Chapter_LevelFail : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
