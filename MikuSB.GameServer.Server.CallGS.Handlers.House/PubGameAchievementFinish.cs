using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("PubGameAchievementFinish")]
public class PubGameAchievementFinish : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int id = PubAttr.Num(root["nId"]);
		if (id <= 0)
		{
			id = PubAttr.Num(root["nAchId"]);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (id > 0)
		{
			await HouseAchievementClaim.MarkCompleteAsync(connection, id, sync);
		}
		await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"PubGameAchievementFinish\"}", sync);
	}
}
