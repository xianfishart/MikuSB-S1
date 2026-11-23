using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ThrowGameEnter")]
public class ThrowGameEnter : IHouseFuncHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		int modeType = ((root != null) ? HouseJson.NumField(root, "nModeType") : 0);
		int girlId = ((root != null) ? HouseJson.NumField(root, "nGirlid") : 0);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "ThrowGameEnter",
			["nSeed"] = Random.Next(1, 1000000000),
			["nModeType"] = modeType,
			["nGirlid"] = girlId,
			["nGirlId"] = girlId
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp));
	}
}
