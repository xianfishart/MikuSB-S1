using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers;

internal static class PortVigor
{
	public static bool TrySpend(PlayerInstance player, uint amount, NtfSyncPlayer sync)
	{
		if (player.Data.Vigor < amount)
		{
			return false;
		}
		player.Data.Vigor -= amount;
		// 第 432 节：消耗感知同步累积智识巡游度（感知巡游）。
		MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.AccrueFromVigor(player, amount, sync);
		sync.Core[2u] = player.Data.Vigor;
		return true;
	}

	public static void Set(PlayerInstance player, uint value, NtfSyncPlayer sync)
	{
		player.Data.Vigor = value;
		sync.Core[2u] = value;
	}

	public static void Add(PlayerInstance player, uint amount, NtfSyncPlayer sync)
	{
		player.AddCurrency(4u, amount, sync);
	}
}
