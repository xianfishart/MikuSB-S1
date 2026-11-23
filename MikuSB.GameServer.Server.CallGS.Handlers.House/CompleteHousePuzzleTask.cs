using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("CompleteHousePuzzleTask")]
public class CompleteHousePuzzleTask : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int index = HouseJson.NumField(root, "Index");
		if (index > 0)
		{
			await HouseProgressHelper.SetAttr(connection, (uint)(60120 + index), 1u);
		}
		await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"CompleteHousePuzzleTaskSuccess\"}");
	}
}
