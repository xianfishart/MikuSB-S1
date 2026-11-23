using MikuSB.Data;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

internal static class RogueCurrency
{
	public static bool TrySpend(PlayerInstance player, uint moneyType, uint amount, NtfSyncPlayer sync, out string error)
	{
		error = "tip.gold_not_enough";
		if (amount == 0)
		{
			return true;
		}
		PlayerAttr orCreate = player.Attributes.GetOrCreate(1u, AttrIds.Currency.GetSid(moneyType));
		if (orCreate.Val < amount)
		{
			return false;
		}
		orCreate.Val -= amount;
		player.Attributes.SyncTo(sync, orCreate);
		return true;
	}
}
