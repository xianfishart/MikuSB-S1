using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Internationalization;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("giveall", "Game.Command.GiveAll.Desc", "Game.Command.GiveAll.Usage", new string[] { "ga" }, new PermEnum[]
{
	PermEnum.Admin,
	PermEnum.Support
})]
public class CommandGiveAll : ICommands
{
	[CommandMethod("weapon")]
	public async ValueTask GiveAllWeapon(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int num4;
		int level = default;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		level = Math.Clamp(level, 1, 90);
		PlayerInstance player = arg.Target.Player;
		List<GameWeaponInfo> weapons = new List<GameWeaponInfo>();
		if (detail == -1)
		{
			foreach (WeaponExcel config in GameData.WeaponData.Values)
			{
				GameWeaponInfo weapon = await player.InventoryManager.AddWeaponItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, (uint)level, sendPacket: false);
				if (weapon != null)
				{
					weapons.Add(weapon);
				}
			}
		}
		else
		{
			GameWeaponInfo weapon2 = await player.InventoryManager.AddWeaponItem(ItemTypeEnum.TYPE_WEAPON, (uint)detail, (uint)particular, 1u, (uint)level, sendPacket: false);
			if (weapon2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Weapon")));
				return;
			}
			weapons.Add(weapon2);
		}
		if (weapons.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(weapons));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.Weapon"), weapons.Count.ToString()));
	}

	[CommandMethod("card")]
	public async ValueTask GiveAllSupportCard(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<GameSupportCardInfo> supportCards = new List<GameSupportCardInfo>();
		if (detail == -1)
		{
			foreach (SupportCardExcel config in GameData.SupportCardData)
			{
				GameSupportCardInfo supportCard = await player.InventoryManager.AddSupportCardItem(config.Detail, config.Particular, config.Level, (uint)level, sendPacket: false);
				if (supportCard != null)
				{
					supportCards.Add(supportCard);
				}
			}
		}
		else
		{
			GameSupportCardInfo supportCard2 = await player.InventoryManager.AddSupportCardItem((uint)detail, (uint)particular, 1u, (uint)level, sendPacket: false);
			if (supportCard2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.SupportCard")));
				return;
			}
			supportCards.Add(supportCard2);
		}
		if (supportCards.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(supportCards));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.SupportCard"), supportCards.Count.ToString()));
	}

	[CommandMethod("weaponskin")]
	public async ValueTask GiveAllWeaponSkin(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<BaseGameItemInfo> weaponSkins = new List<BaseGameItemInfo>();
		if (detail == -1)
		{
			foreach (WeaponSkinExcel config in GameData.WeaponSkinData.Values)
			{
				BaseGameItemInfo weaponSkin = await player.InventoryManager.AddWeaponSkinItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (weaponSkin != null)
				{
					weaponSkins.Add(weaponSkin);
				}
			}
		}
		else
		{
			BaseGameItemInfo weaponSkin2 = await player.InventoryManager.AddWeaponSkinItem(ItemTypeEnum.TYPE_WEAPON, (uint)detail, (uint)particular, 1u, sendPacket: false);
			if (weaponSkin2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.WeaponSkin")));
				return;
			}
			weaponSkins.Add(weaponSkin2);
		}
		if (weaponSkins.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(weaponSkins));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.WeaponSkin"), weaponSkins.Count.ToString()));
	}

	[CommandMethod("profile")]
	public async ValueTask GiveAllProfile(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('g');
		int genre = default;
		int num6;
		if (num5.HasValue)
		{
			genre = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<BaseGameItemInfo> profileItems = new List<BaseGameItemInfo>();
		if (detail == -1)
		{
			foreach (ProfileExcel config in GameData.ProfileData.Values)
			{
				BaseGameItemInfo profile = await player.InventoryManager.AddProfileItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (profile != null)
				{
					profileItems.Add(profile);
				}
			}
		}
		else
		{
			BaseGameItemInfo profile2 = await player.InventoryManager.AddProfileItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, sendPacket: false);
			if (profile2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Profile")));
				return;
			}
			profileItems.Add(profile2);
		}
		if (profileItems.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(profileItems));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.Profile"), profileItems.Count.ToString()));
	}

	[CommandMethod("skinpart")]
	public async ValueTask GiveAllSkinPart(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('g');
		int genre = default;
		int num6;
		if (num5.HasValue)
		{
			genre = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<BaseGameItemInfo> skinPartItems = new List<BaseGameItemInfo>();
		if (detail == -1)
		{
			foreach (CardSkinPartsExcel config in GameData.CardSkinPartsData.Values)
			{
				BaseGameItemInfo skinPart = await player.InventoryManager.AddSkinPartItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (skinPart != null)
				{
					skinPartItems.Add(skinPart);
				}
			}
		}
		else
		{
			BaseGameItemInfo skinPart2 = await player.InventoryManager.AddSkinPartItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, sendPacket: false);
			if (skinPart2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.SkinPart")));
				return;
			}
			skinPartItems.Add(skinPart2);
		}
		if (skinPartItems.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(skinPartItems));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.SkinPart"), skinPartItems.Count.ToString()));
	}

	[CommandMethod("call")]
	public async ValueTask GiveAllCallItem(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('g');
		int genre = default;
		int num6;
		if (num5.HasValue)
		{
			genre = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<BaseGameItemInfo> callItems = new List<BaseGameItemInfo>();
		if (detail == -1)
		{
			foreach (CallItemExcel config in GameData.CallItemData.Values)
			{
				BaseGameItemInfo callItem = await player.InventoryManager.AddCallItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (callItem != null)
				{
					callItems.Add(callItem);
				}
			}
		}
		else
		{
			BaseGameItemInfo callItem2 = await player.InventoryManager.AddCallItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, sendPacket: false);
			if (callItem2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.CallItem")));
				return;
			}
			callItems.Add(callItem2);
		}
		if (callItems.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(callItems));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.CallItem"), callItems.Count.ToString()));
	}

	[CommandMethod("weaponpart")]
	public async ValueTask GiveAllWeaponPart(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('g');
		int genre = default;
		int num6;
		if (num5.HasValue)
		{
			genre = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<BaseGameItemInfo> weaponPartItems = new List<BaseGameItemInfo>();
		if (detail == -1)
		{
			foreach (WeaponPartsExcel config in GameData.WeaponPartsData.Values)
			{
				BaseGameItemInfo weaponPart = await player.InventoryManager.AddWeaponPartItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (weaponPart != null)
				{
					weaponPartItems.Add(weaponPart);
				}
			}
		}
		else
		{
			BaseGameItemInfo weaponPart2 = await player.InventoryManager.AddWeaponPartItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, sendPacket: false);
			if (weaponPart2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.WeaponPart")));
				return;
			}
			weaponPartItems.Add(weaponPart2);
		}
		if (weaponPartItems.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(weaponPartItems));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.WeaponPart"), weaponPartItems.Count.ToString()));
	}

	[CommandMethod("skin")]
	public async ValueTask GiveAllSkin(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('g');
		int genre = default;
		int num6;
		if (num5.HasValue)
		{
			genre = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<GameSkinInfo> skinItems = new List<GameSkinInfo>();
		if (detail == -1)
		{
			foreach (CardSkinExcel config in GameData.CardSkinData.Values)
			{
				GameSkinInfo skin = await player.InventoryManager.AddSkinItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (skin != null)
				{
					skinItems.Add(skin);
				}
			}
		}
		else
		{
			GameSkinInfo skin2 = await player.InventoryManager.AddSkinItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, sendPacket: false);
			if (skin2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Skin")));
				return;
			}
			skinItems.Add(skin2);
		}
		if (skinItems.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(skinItems));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.Skin"), skinItems.Count.ToString()));
	}

	[CommandMethod("furniture")]
	public async ValueTask GiveAllHouseFurniture(CommandArg arg)
	{
		if (!(await arg.CheckOnlineTarget()))
		{
			return;
		}
		int? num = await arg.GetOption('p');
		int particular = default;
		int num2;
		if (num.HasValue)
		{
			particular = num.GetValueOrDefault();
			num2 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		int? num3 = await arg.GetOption('l');
		int level = default;
		int num4;
		if (num3.HasValue)
		{
			level = num3.GetValueOrDefault();
			num4 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num4 = 1;
		}
		if (num4 != 0)
		{
			return;
		}
		int? num5 = await arg.GetOption('g');
		int genre = default;
		int num6;
		if (num5.HasValue)
		{
			genre = num5.GetValueOrDefault();
			num6 = ((1 == 0) ? 1 : 0);
		}
		else
		{
			num6 = 1;
		}
		if (num6 != 0)
		{
			return;
		}
		int detail = arg.GetInt(0);
		PlayerInstance player = arg.Target.Player;
		List<BaseGameItemInfo> furnitureItems = new List<BaseGameItemInfo>();
		if (detail == -1)
		{
			foreach (DormGiftExcel config in GameData.DormGiftData.Values)
			{
				BaseGameItemInfo furniture = await player.InventoryManager.AddHouseFurnitureItem((ItemTypeEnum)config.Genre, config.Detail, config.Particular, config.Level, sendPacket: false);
				if (furniture != null)
				{
					furnitureItems.Add(furniture);
				}
			}
		}
		else
		{
			BaseGameItemInfo furniture2 = await player.InventoryManager.AddHouseFurnitureItem((ItemTypeEnum)genre, (uint)detail, (uint)particular, (uint)level, sendPacket: false);
			if (furniture2 == null)
			{
				await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.NotFound", I18NManager.Translate("Word.Furniture")));
				return;
			}
			furnitureItems.Add(furniture2);
		}
		if (furnitureItems.Count > 0)
		{
			await player.SendPacket(new PacketNtfCallScript(furnitureItems));
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await arg.SendMsg(I18NManager.Translate("Game.Command.GiveAll.GiveAllItems", I18NManager.Translate("Word.Furniture"), furnitureItems.Count.ToString()));
	}
}
