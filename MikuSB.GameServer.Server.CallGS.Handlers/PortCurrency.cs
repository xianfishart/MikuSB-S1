using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers;

internal static class PortCurrency
{
	public static bool TrySpend(PlayerInstance player, uint moneyType, uint amount, NtfSyncPlayer sync, out string error)
	{
		error = "tip.gold_not_enough";
		if (amount == 0)
		{
			return true;
		}
		if (moneyType == 4)
		{
			if (player.Data.Vigor < amount)
			{
				error = "tip.vigor_not_enough";
				return false;
			}
			player.Data.Vigor -= amount;
			// 第 432 节：消耗感知同步累积智识巡游度（感知巡游）。
			MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.AccrueFromVigor(player, amount, sync);
			sync.Core[2u] = player.Data.Vigor;
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

	public static uint GetBalance(PlayerInstance player, uint moneyType)
	{
		return (moneyType == 4) ? player.Data.Vigor : player.Attributes.GetValue(1u, AttrIds.Currency.GetSid(moneyType));
	}

	public static void Add(PlayerInstance player, uint moneyType, uint amount, NtfSyncPlayer sync)
	{
		player.AddCurrency(moneyType, amount, sync);
	}

	public static bool TrySpendItem(PlayerInstance player, IReadOnlyList<uint> gdpl, uint count, out string error)
	{
		error = "tip.NotEnoughItem";
		if (count == 0)
		{
			return true;
		}
		if (gdpl.Count < 4)
		{
			return false;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl(gdpl[0], gdpl[1], gdpl[2], gdpl[3]);
		BaseGameItemInfo baseGameItemInfo = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
		if (baseGameItemInfo == null || baseGameItemInfo.ItemCount < count)
		{
			return false;
		}
		baseGameItemInfo.ItemCount -= count;
		return true;
	}

	public static void Persist(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
	}
}
