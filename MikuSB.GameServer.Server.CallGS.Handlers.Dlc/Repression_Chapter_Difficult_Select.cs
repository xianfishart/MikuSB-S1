using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Repression_Chapter_Difficult_Select")]
public class Repression_Chapter_Difficult_Select : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Repression_Chapter_Difficult_Select", "{}");
	}
}
