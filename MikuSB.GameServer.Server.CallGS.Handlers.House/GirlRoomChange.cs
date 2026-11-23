using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GirlRoomChange")]
public class GirlRoomChange : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int girlId = HouseJson.NumField(root, "GirlId");
			int roomId = HouseJson.NumField(root, "RoomId");
			int oldRoomId = ((girlId > 0) ? HouseAttr.FindGirlRoom(connection.Player, girlId) : 0);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (girlId > 0 && roomId > 0)
			{
				await HouseAttr.MoveGirlIntoRoomAsync(connection, girlId, roomId, sync);
			}
			else if (girlId > 0)
			{
				oldRoomId = await HouseAttr.MoveGirlOutOfRoomAsync(connection, girlId, sync);
			}
			JsonObject rsp = new JsonObject
			{
				["FuncName"] = "GirlRoomChangeSuccess",
				["GirlId"] = girlId,
				["OldRoomId"] = oldRoomId,
				["NewRoomId"] = roomId
			};
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
		}
	}
}
