using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3D_UnlockJourneyLine")]
public class Rogue3D_UnlockJourneyLine : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		uint lineId = 0u;
		try
		{
			lineId = Rogue3DAttr.ReadUInt(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nLineID"]);
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		if (player != null && lineId != 0)
		{
			uint sid = 2101 + lineId / 8;
			int shift = (int)(lineId % 8 * 4);
			uint cur = Rogue3DAttr.Read(player, sid);
			Rogue3DAttr.Write(player, sid, Rogue3DAttr.SetField(cur, shift, 4, 1u), sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "Rogue3D_UnlockJourneyLine", new JsonObject { ["nLineID"] = lineId }.ToJsonString(), sync);
	}
}
