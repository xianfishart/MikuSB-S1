using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Repression_Chapter_LevelScoreSelect")]
public class Repression_Chapter_LevelScoreSelect : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Repression_Chapter_LevelScoreSelect", "{}");
	}
}
