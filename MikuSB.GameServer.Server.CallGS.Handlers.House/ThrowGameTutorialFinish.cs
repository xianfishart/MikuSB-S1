using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ThrowGameTutorialFinish")]
public class ThrowGameTutorialFinish : IHouseFuncHandler
{
	private const uint HouseBeachInfoStart = 17000u;

	private const uint ThrowGameTutorialOffset = 10u;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int index = Math.Max(0, HouseJson.NumField(root, "Index"));
			NtfSyncPlayer sync = new NtfSyncPlayer();
			await HouseAttr.SetAsync(connection, (uint)(17010 + index), 1u, sync);
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
		}
	}
}
