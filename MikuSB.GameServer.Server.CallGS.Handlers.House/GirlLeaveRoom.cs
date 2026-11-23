using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GirlLeaveRoom")]
public class GirlLeaveRoom : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			HouseJson.NumField(root, "BedroomId");
			int girlId = HouseJson.NumField(root, "GirlId");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (girlId > 0)
			{
				await HouseAttr.ClearGirlSlotsAsync(connection, girlId, sync);
				await HouseAttr.SetAsync(connection, HouseAttr.GirlRoomNumSid(girlId), 100u, sync);
			}
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
		}
	}
}
