using System;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("PlayerCharge_Switch")]
public class PlayerCharge_Switch : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		uint value = (uint.TryParse(param?.Trim('"'), out var n) ? Math.Min(n, 1u) : 0u);
		uint claimed = player.GetAttr(0u, 19u);
		claimed = ((value == 1) ? (claimed | 0x80000000u) : (claimed & 0x7FFFFFFF));
		player.SetAttr(0u, 19u, claimed);
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(0u, 19u)] = claimed;
		sync.Custom[player.ToShiftedAttrKey(0u, 19u)] = claimed;
		await CallGSRouter.SendScript(connection, "PlayerCharge_Switch", "null", sync);
	}
}
