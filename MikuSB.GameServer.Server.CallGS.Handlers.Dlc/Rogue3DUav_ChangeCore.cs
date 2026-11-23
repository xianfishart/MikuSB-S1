using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3DUav_ChangeCore")]
public class Rogue3DUav_ChangeCore : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		uint coreId = 0u;
		try
		{
			coreId = Rogue3DAttr.ReadUInt(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nCoreId"]);
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		if (player != null && coreId != 0)
		{
			Rogue3DAttr.Write(player, 3102u, coreId, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "Rogue3DUav_ChangeCore", "{}", sync);
	}
}
