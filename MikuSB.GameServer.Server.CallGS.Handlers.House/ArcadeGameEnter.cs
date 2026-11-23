using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ArcadeGameEnter")]
public class ArcadeGameEnter : IHouseFuncHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param)
	{
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "ArcadeGameEnter",
			["nSeed"] = Random.Next(1, 1000000000)
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp));
	}
}
