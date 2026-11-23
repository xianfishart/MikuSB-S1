using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("SavePuzzleMapState")]
public class SavePuzzleMapState : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonObject root = HouseJson.ParseObject(param);
			if (root != null)
			{
				JsonObject request = (root["tbParam"] as JsonObject) ?? root;
				player.SetStrAttr(value: request["MapState"]?.ToJsonString() ?? "[]", gid: 101u, sid: DormPuzzleRequests.MapStateSid);
				DatabaseHelper.SaveDatabaseType(player.Data);
				await CallGSRouter.SendScript(connection, "House_Request", "{\"bSuccess\":true}");
			}
		}
	}
}
