using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GachaTry;

[CallGSApi("GachaTry_EnterLevel")]
public class GachaTry_EnterLevel : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, "GachaTry_EnterLevel", "{}");
	}
}
