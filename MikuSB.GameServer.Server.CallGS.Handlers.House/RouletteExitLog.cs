using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("RouletteExitLog")]
public class RouletteExitLog : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root));
		}
	}
}
