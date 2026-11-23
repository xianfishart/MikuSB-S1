using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3DScoreModel_ScoreLevelSettlement")]
public class Rogue3DScoreModel_ScoreLevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_ScoreLevelSettlement", "{\"bSuccess\":true,\"tbReward\":[]}");
	}
}
