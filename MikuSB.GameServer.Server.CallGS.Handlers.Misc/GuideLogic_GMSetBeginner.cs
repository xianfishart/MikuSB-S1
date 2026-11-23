using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("GuideLogic_GMSetBeginner")]
public class GuideLogic_GMSetBeginner : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
