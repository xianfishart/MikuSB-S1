using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Game.Inventory;

internal static class ProfileUnlock
{
	public static async Task Ensure(PlayerInstance player, bool sendPacket)
	{
		InventoryManager inv = player.InventoryManager;
		if (inv?.InventoryData == null)
		{
			return;
		}
		int added = 0;
		foreach (CardExcel card in GameData.CardData.Values)
		{
			if (player.CharacterManager.GetCharacterGDPL(ItemTypeEnum.TYPE_CARD, (int)card.Detail, (int)card.Particular) != null)
			{
				int num = added;
				added = num + await GrantProfiles(player, inv, card.Profile ?? new List<List<int>>(), sendPacket);
			}
		}
		foreach (CardSkinExcel skin in GameData.CardSkinData.Values)
		{
			if (inv.GetSkinItemGDPL(ItemTypeEnum.TYPE_CARD_SKIN, skin.Detail, skin.Particular, skin.Level) != null)
			{
				int num2 = added;
				added = num2 + await GrantProfiles(player, inv, skin.Profile ?? new List<List<int>>(), sendPacket);
			}
		}
		int num3 = added;
		added = num3 + await GrantProfiles(player, inv, new List<List<int>>(1)
		{
			new List<int>(5) { 9, 10, 1, 1, 1 }
		}, sendPacket);
		(int D, int P, int L)[] favorAvatars = new (int, int, int)[21]
		{
			(2, 5, 1),
			(2, 6, 1),
			(2, 7, 1),
			(2, 8, 1),
			(2, 9, 1),
			(2, 10, 1),
			(2, 11, 1),
			(2, 12, 1),
			(2, 13, 1),
			(2, 14, 1),
			(2, 15, 1),
			(2, 16, 1),
			(2, 17, 1),
			(2, 27, 1),
			(2, 29, 1),
			(2, 34, 1),
			(2, 47, 1),
			(2, 60, 1),
			(2, 79, 1),
			(2, 100, 1),
			(2, 104, 1)
		};
		(int D, int P, int L)[] array = favorAvatars;
		for (int i = 0; i < array.Length; i++)
		{
			var (d, p, l) = array[i];
			if (await inv.AddProfileItem(ItemTypeEnum.TYPE_PROFILE, (uint)d, (uint)p, (uint)l, sendPacket) != null)
			{
				added++;
			}
		}
		if (player.Data.Level >= 20)
		{
			int num4 = added;
			added = num4 + await GrantProfile(inv, 13, 1u, 4u, 1u, sendPacket);
		}
		if (player.Data.Level >= 50)
		{
			int num5 = added;
			added = num5 + await GrantProfile(inv, 13, 1u, 7u, 1u, sendPacket);
		}
		int num6 = added;
		added = num6 + await GrantProfile(inv, 13, 1u, 22u, 1u, sendPacket);
		int num7 = added;
		added = num7 + await GrantProfile(inv, 13, 1u, 23u, 1u, sendPacket);
		foreach (ProfileExcel cover in GameData.ProfileData.Values)
		{
			if (cover.Genre == 12 && await inv.AddProfileItem((ItemTypeEnum)cover.Genre, cover.Detail, cover.Particular, cover.Level, sendPacket) != null)
			{
				added++;
			}
		}
		if (added > 0)
		{
			DatabaseHelper.SaveDatabaseType(inv.InventoryData);
		}
		try
		{
			File.AppendAllText("D:\\DSH\\.tools\\profile_unlock.log", $"{DateTime.Now:HH:mm:ss} uid={player.Uid} added={added} items={inv.InventoryData.Items.Count}\n");
		}
		catch
		{
		}
	}

	private static async Task<int> GrantProfile(InventoryManager inv, int genre, uint detail, uint particular, uint level, bool sendPacket)
	{
		return (await inv.AddProfileItem((ItemTypeEnum)genre, detail, particular, level, sendPacket) != null) ? 1 : 0;
	}

	private static async Task<int> GrantProfiles(PlayerInstance player, InventoryManager inv, List<List<int>> profiles, bool sendPacket)
	{
		int added = 0;
		foreach (List<int> p in profiles)
		{
			if (p.Count >= 4)
			{
				ItemTypeEnum genre = (ItemTypeEnum)p[0];
				if (genre >= ItemTypeEnum.TYPE_PROFILE && genre <= ItemTypeEnum.TYPE_ANALYST && await inv.AddProfileItem(genre, (uint)p[1], (uint)p[2], (uint)p[3], sendPacket) != null)
				{
					added++;
				}
			}
		}
		return added;
	}
}
