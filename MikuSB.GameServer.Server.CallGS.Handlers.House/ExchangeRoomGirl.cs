using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ExchangeRoomGirl")]
public class ExchangeRoomGirl : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		int roomId1 = HouseJson.NumField(root, "RoomId1");
		int roomId2 = HouseJson.NumField(root, "RoomId2");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (roomId1 > 0 && roomId2 > 0 && roomId1 != roomId2)
		{
			uint slot1 = HouseAttr.BedroomSlotSid(roomId1);
			uint slot2 = HouseAttr.BedroomSlotSid(roomId2);
			uint girl1 = HouseAttr.Read(connection.Player, slot1);
			uint girl2 = HouseAttr.Read(connection.Player, slot2);
			await HouseAttr.SetAsync(connection, slot1, girl2, sync);
			await HouseAttr.SetAsync(connection, slot2, girl1, sync);
			if (girl1 != 0)
			{
				await HouseAttr.SetAsync(connection, HouseAttr.GirlRoomNumSid((int)girl1), (uint)roomId2, sync);
			}
			if (girl2 != 0)
			{
				await HouseAttr.SetAsync(connection, HouseAttr.GirlRoomNumSid((int)girl2), (uint)roomId1, sync);
			}
		}
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
	}
}
