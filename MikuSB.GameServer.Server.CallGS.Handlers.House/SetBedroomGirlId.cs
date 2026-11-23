using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("SetBedroomGirlId")]
public class SetBedroomGirlId : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int bedroomId = HouseJson.NumField(root, "BedroomId");
			int girlId = HouseJson.NumField(root, "GirlId");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (bedroomId > 0 && girlId > 0)
			{
				await HouseAttr.MoveGirlIntoRoomAsync(connection, girlId, bedroomId, sync);
			}
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
		}
	}
}
