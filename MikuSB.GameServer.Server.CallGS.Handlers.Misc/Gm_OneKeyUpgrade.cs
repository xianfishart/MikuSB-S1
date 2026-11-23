using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("Gm_OneKeyUpgrade")]
public class Gm_OneKeyUpgrade : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonObject root = JsonNode.Parse(param)?.AsObject();
			int level = ((root != null) ? ReadInt(root["level"]) : 0);
			if (level > 0)
			{
				level = Math.Clamp(level, 1, 80);
				player.Data.Level = (uint)level;
				player.Data.Exp = 0;
				DatabaseHelper.SaveDatabaseType(player.Data);
				NtfSyncPlayer sync = new NtfSyncPlayer();
				sync.Core[0u] = player.Data.Level;
				sync.Core[1u] = 0u;
				await connection.SendPacket(1004, sync, 0);
			}
		}
	}

	private static int ReadInt(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
