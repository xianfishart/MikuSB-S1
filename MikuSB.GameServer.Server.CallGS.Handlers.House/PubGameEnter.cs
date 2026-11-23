using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("PubGameEnter")]
public class PubGameEnter : IHouseFuncHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param)
	{
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "PubGameEnter",
			["nSeed"] = Random.Next(1, 1000000000),
			["nModeType"] = 1,
			["bIsGuide"] = false,
			["bHasTry"] = false
		};
		await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString());
	}
}
