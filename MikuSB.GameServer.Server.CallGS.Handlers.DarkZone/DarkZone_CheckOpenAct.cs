using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("DarkZone_CheckOpenAct")]
public class DarkZone_CheckOpenAct : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonObject act = DarkZoneData.Act();
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			JsonObject rsp = new JsonObject
			{
				["bOpen"] = true,
				["nId"] = ((act == null) ? 1 : DarkZoneData.ReadInt(act["ID"])),
				["nBeginTime"] = now - 86400,
				["nEndTime"] = now + 2592000
			};
			await CallGSRouter.SendScript(connection, "DarkZone_CheckOpenAct", rsp.ToJsonString(), new NtfSyncPlayer());
		}
	}
}
