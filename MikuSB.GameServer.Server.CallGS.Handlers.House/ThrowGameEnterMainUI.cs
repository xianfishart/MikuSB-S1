using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GameEnterMainUI")]
public class ThrowGameEnterMainUI : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "GameEnterMainUI",
			["tblockGirlList"] = new JsonArray()
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp));
	}
}
