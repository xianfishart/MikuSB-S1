using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("GuideLogic_WriteGuideLog")]
public class GuideLogic_WriteGuideLog : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
