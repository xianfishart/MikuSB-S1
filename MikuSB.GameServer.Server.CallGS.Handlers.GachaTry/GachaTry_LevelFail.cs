using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GachaTry;

[CallGSApi("GachaTry_LevelFail")]
public class GachaTry_LevelFail : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, "GachaTry_LevelFail", "{}");
	}
}
