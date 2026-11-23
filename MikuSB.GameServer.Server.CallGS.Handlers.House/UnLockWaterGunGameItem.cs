using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("UnLockWaterGunGameItem")]
public class UnLockWaterGunGameItem : IHouseFuncHandler
{
	private const uint HouseHotSpringInfoStart = 15000u;

	private const uint WaterGunItemCollectBegin = 17u;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonNode jsonNode = root["tbItem"];
		if (jsonNode is JsonArray items)
		{
			Dictionary<uint, uint> touched = new Dictionary<uint, uint>();
			foreach (JsonNode node in items)
			{
				int itemId = HouseJson.ToInt(node);
				if ((itemId >= 0 && itemId <= 60) || 1 == 0)
				{
					int offset = itemId / 30;
					int bit = itemId % 30;
					uint sid = (uint)(15017 + offset);
					uint cur = (touched.TryGetValue(sid, out var existing) ? existing : HouseAttr.Read(connection.Player, sid));
					touched[sid] = cur | (uint)(1 << bit);
				}
			}
			foreach (var (sid2, value) in touched)
			{
				await HouseAttr.SetAsync(connection, sid2, value, sync);
			}
		}
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
	}
}
