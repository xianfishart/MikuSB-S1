using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ArcadeGameLogSettlement")]
public class ArcadeGameLogSettlement : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject rsp = new JsonObject { ["FuncName"] = "ArcadeGameLogSettlement" };
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp));
	}
}
