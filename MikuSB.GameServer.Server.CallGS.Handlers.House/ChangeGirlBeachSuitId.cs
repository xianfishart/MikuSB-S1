using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ChangeGirlBeachSuitId")]
public class ChangeGirlBeachSuitId : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int npcId = HouseJson.NumField(root, "NpcId");
			int suitId = HouseJson.NumField(root, "SuitId");
			NtfSyncPlayer sync = new NtfSyncPlayer();
			if (npcId > 0)
			{
				await HouseAttr.SetAsync(connection, (uint)(npcId * 50 + 8), (uint)Math.Max(0, suitId), sync, deleteIfZero: false, sendImmediate: true);
			}
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
		}
	}
}
