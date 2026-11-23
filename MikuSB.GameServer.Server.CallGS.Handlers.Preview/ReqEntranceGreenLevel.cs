using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Preview;

[CallGSApi("ReqEntranceGreenLevel")]
public class ReqEntranceGreenLevel : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "ReqEntranceGreenLevel", "[]");
	}
}
