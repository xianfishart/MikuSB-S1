using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("QuickCollectDrop")]
public class QuickCollectDrop : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		JsonObject request = (root["tbParam"] as JsonObject) ?? root;
		JsonArray list = request["tbPosIdList"] as JsonArray;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray clear = new JsonArray();
		JsonArray clips = new JsonArray();
		if (list != null)
		{
			foreach (JsonNode node in list)
			{
				int posId = ((node is JsonValue v && v.TryGetValue<int>(out var n)) ? n : 0);
				if (posId > 0)
				{
					var (ok, _, clipId) = await HouseDropService.Collect(connection, posId, sync);
					if (ok)
					{
						clear.Add(posId);
						clips.Add(new JsonArray { clipId, 1 });
					}
				}
			}
		}
		JsonObject response = new JsonObject
		{
			["FuncName"] = "QuickGetRandomClipSuccess",
			["bSuccess"] = true,
			["tbClearList"] = clear,
			["tbRetClip"] = clips
		};
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}
}
