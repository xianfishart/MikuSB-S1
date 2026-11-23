using System;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Challenge;

[CallGSApi("WorldBossLogic_CheckOpen")]
public class WorldBossLogic_CheckOpen : ICallGSHandler
{
	private const uint Gid = 179u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		uint actId = ((DateTime.Now.Hour >= 12) ? 1u : 2u);
		player.SetAttr(179u, 0u, 1u);
		player.SetAttr(179u, 1u, actId);
		player.SetAttr(179u, 2u, 5u);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "WorldBossLogic_CheckOpen", "{\"nOpen\":1}");
	}
}
