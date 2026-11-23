using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("CollectDrop")]
public class CollectDrop : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			JsonObject request = (root["tbParam"] as JsonObject) ?? root;
			int posId = HouseJson.NumField(request, "PosId");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			(bool Ok, int PosId, int ClipId) tuple = await HouseDropService.Collect(connection, posId, sync);
			bool ok = tuple.Ok;
			int clipId = tuple.ClipId;
			JsonObject response = new JsonObject
			{
				["FuncName"] = "GetRandomClipSuccess",
				["bSuccess"] = ok,
				["PosId"] = posId,
				["ClipId"] = clipId
			};
			await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
		}
	}
}
