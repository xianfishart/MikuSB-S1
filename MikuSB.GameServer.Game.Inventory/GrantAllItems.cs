using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Inventory;

internal static class GrantAllItems
{
	private static readonly Logger Logger = new Logger("GrantAll");

	public static Task GrantMissing(PlayerInstance player, bool sendPackets = true)
	{
		return Task.CompletedTask;
	}

	private static async Task GrantMissingLegacy(PlayerInstance player, bool sendPackets = true)
	{
		InventoryManager inv = player.InventoryManager;
		if (inv == null || inv.InventoryData == null)
		{
			Logger.Error($"grant skipped: inv null={inv == null}");
			return;
		}
		InventoryData inventoryData = inv.InventoryData;
		if (inventoryData.Weapons == null)
		{
			inventoryData.Weapons = new Dictionary<uint, GameWeaponInfo>();
		}
		inventoryData = inv.InventoryData;
		if (inventoryData.Items == null)
		{
			inventoryData.Items = new Dictionary<uint, BaseGameItemInfo>();
		}
		inventoryData = inv.InventoryData;
		if (inventoryData.SupportCards == null)
		{
			inventoryData.SupportCards = new Dictionary<uint, GameSupportCardInfo>();
		}
		List<GameWeaponInfo> weapons = new List<GameWeaponInfo>();
		List<BaseGameItemInfo> parts = new List<BaseGameItemInfo>();
		List<GameSupportCardInfo> cards = new List<GameSupportCardInfo>();
		List<BaseGameItemInfo> analystSuits = new List<BaseGameItemInfo>();
		List<BaseGameItemInfo> weaponSkins = new List<BaseGameItemInfo>();
		foreach (WeaponExcel cfg in GameData.WeaponData.Values)
		{
			if (cfg == null)
			{
				continue;
			}
			ItemTypeEnum genre = (ItemTypeEnum)cfg.Genre;
			if (inv.GetWeaponItemGDPL(genre, cfg.Detail, cfg.Particular, cfg.Level) == null)
			{
				GameWeaponInfo item = await inv.AddWeaponItem(genre, cfg.Detail, cfg.Particular, cfg.Level, 1u, sendPacket: false);
				if (item != null)
				{
					weapons.Add(item);
				}
			}
		}
		foreach (WeaponPartsExcel cfg2 in GameData.WeaponPartsData.Values)
		{
			if (cfg2 == null)
			{
				continue;
			}
			ItemTypeEnum genre2 = (ItemTypeEnum)cfg2.Genre;
			if (inv.GetNormalItemGDPL(genre2, cfg2.Detail, cfg2.Particular, cfg2.Level) == null)
			{
				BaseGameItemInfo item2 = await inv.AddWeaponPartItem(genre2, cfg2.Detail, cfg2.Particular, cfg2.Level, sendPacket: false);
				if (item2 != null)
				{
					parts.Add(item2);
				}
			}
		}
		foreach (SupportCardExcel cfg3 in GameData.SupportCardData)
		{
			if (cfg3 != null && inv.GetSupportCardItemGDPL(ItemTypeEnum.TYPE_SUPPORT, cfg3.Detail, cfg3.Particular, cfg3.Level) == null)
			{
				GameSupportCardInfo item3 = await inv.AddSupportCardItem(cfg3.Detail, cfg3.Particular, cfg3.Level, 1u, sendPacket: false);
				if (item3 != null)
				{
					cards.Add(item3);
				}
			}
		}
		foreach (ProfileExcel cfg4 in GameData.ProfileData.Values)
		{
			if (cfg4 == null || cfg4.Genre != 16)
			{
				continue;
			}
			ItemTypeEnum genre3 = (ItemTypeEnum)cfg4.Genre;
			if (inv.GetNormalItemGDPL(genre3, cfg4.Detail, cfg4.Particular, cfg4.Level) == null)
			{
				BaseGameItemInfo item4 = await inv.AddProfileItem(genre3, cfg4.Detail, cfg4.Particular, cfg4.Level, sendPacket: false);
				if (item4 != null)
				{
					analystSuits.Add(item4);
				}
			}
		}
		foreach (WeaponSkinExcel cfg5 in GameData.WeaponSkinData.Values)
		{
			if (cfg5 == null)
			{
				continue;
			}
			ItemTypeEnum genre4 = (ItemTypeEnum)cfg5.Genre;
			if (inv.GetNormalItemGDPL(genre4, cfg5.Detail, cfg5.Particular, cfg5.Level) == null)
			{
				BaseGameItemInfo item5 = await inv.AddWeaponSkinItem(genre4, cfg5.Detail, cfg5.Particular, cfg5.Level, sendPacket: false);
				if (item5 != null)
				{
					weaponSkins.Add(item5);
				}
			}
		}
		if (sendPackets)
		{
			if (weapons.Count > 0)
			{
				await player.SendPacket(new PacketNtfCallScript(weapons));
			}
			if (parts.Count > 0)
			{
				await player.SendPacket(new PacketNtfCallScript(parts));
			}
			if (cards.Count > 0)
			{
				await player.SendPacket(new PacketNtfCallScript(cards));
			}
			if (analystSuits.Count > 0)
			{
				await player.SendPacket(new PacketNtfCallScript(analystSuits));
			}
			if (weaponSkins.Count > 0)
			{
				await player.SendPacket(new PacketNtfCallScript(weaponSkins));
			}
		}
		Logger.Info($"grant uid={player.Uid} cfgW={GameData.WeaponData.Count} cfgP={GameData.WeaponPartsData.Count} cfgC={GameData.SupportCardData.Count} cfgWS={GameData.WeaponSkinData.Count} invW={inv.InventoryData.Weapons.Count} invI={inv.InventoryData.Items.Count} invC={inv.InventoryData.SupportCards.Count} addedW={weapons.Count} addedP={parts.Count} addedC={cards.Count} addedSuit={analystSuits.Count} addedWS={weaponSkins.Count}");
		if (weapons.Count + parts.Count + cards.Count + analystSuits.Count + weaponSkins.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(inv.InventoryData);
		}
		ItemSanitizer.Cleanup(player);
	}
}
