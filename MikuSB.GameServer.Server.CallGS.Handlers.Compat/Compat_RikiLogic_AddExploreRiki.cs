using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("RikiLogic_AddExploreRiki")]
public class Compat_RikiLogic_AddExploreRiki : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return Task.CompletedTask;
	}
}
