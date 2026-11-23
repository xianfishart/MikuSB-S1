using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("PubGameMulExit")]
public class PubGameMulExit : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"PubGameMulExit\"}");
	}
}
