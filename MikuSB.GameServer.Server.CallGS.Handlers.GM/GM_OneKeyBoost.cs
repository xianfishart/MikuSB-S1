using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Riki;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GM;

[CallGSApi("GM_OneKeyBoost")]
public class GM_OneKeyBoost : ICallGSHandler
{
	private const uint CharacterMaxLevel = 80u;

	private const uint CharacterMaxBreak = 45u;

	private const uint ProLevelMax = 3u;

	private const uint WeaponMaxLevel = 80u;

	private const uint WeaponMaxBreak = 6u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (!AccountData.HasPerm(new PermEnum[1] { PermEnum.Admin }, player.Uid))
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<CharacterInfo> characters = new List<CharacterInfo>();
		List<GameWeaponInfo> weapons = new List<GameWeaponInfo>();
		List<GameSupportCardInfo> supports = new List<GameSupportCardInfo>();
		List<BaseGameItemInfo> cosmetics = new List<BaseGameItemInfo>();
		List<BaseGameItemInfo> weaponParts = new List<BaseGameItemInfo>();
		if (player.Data.Level < 100)
		{
			player.AddPlayerExperience(200000000u, sync);
		}
		foreach (CardExcel card in GameData.CardData.Values.Where((CardExcel x) => x.Genre == 1))
		{
			if (!ItemSanitizer.IsBanned(card.Genre, card.Detail, card.Particular, card.Level))
			{
				await player.CharacterManager.AddCharacter(ItemTypeEnum.TYPE_CARD, card.Detail, card.Particular, 1u, 5u, sendPacket: false);
			}
		}
		foreach (CharacterInfo character in player.CharacterManager.CharacterData.Characters)
		{
			character.Level = 80u;
			character.Break = 45u;
			character.ProLevel = 3u;
			character.Spines = new List<uint>(6) { 511u, 511u, 511u, 511u, 511u, 511u };
			characters.Add(character);
		}
		foreach (WeaponExcel config in GameData.WeaponData.Values)
		{
			if (!ItemSanitizer.IsBanned(config.Genre, config.Detail, config.Particular, config.Level))
			{
				ItemTypeEnum type = (ItemTypeEnum)config.Genre;
				if (player.InventoryManager.GetWeaponItemGDPL(type, config.Detail, config.Particular, config.Level) == null)
				{
					await player.InventoryManager.AddWeaponItem(type, config.Detail, config.Particular, 1u, 80u, sendPacket: false);
				}
			}
		}
		foreach (GameWeaponInfo weapon in player.InventoryManager.InventoryData.Weapons.Values)
		{
			weapon.Level = 80u;
			weapon.Break = 6u;
			weapons.Add(weapon);
		}
		foreach (WeaponPartsExcel config2 in GameData.WeaponPartsData.Values)
		{
			if (ItemSanitizer.IsBanned(config2.Genre, config2.Detail, config2.Particular, config2.Level))
			{
				continue;
			}
			ItemTypeEnum type2 = (ItemTypeEnum)config2.Genre;
			if (player.InventoryManager.GetNormalItemGDPL(type2, config2.Detail, config2.Particular, config2.Level) == null)
			{
				BaseGameItemInfo item = await player.InventoryManager.AddWeaponPartItem(type2, config2.Detail, config2.Particular, config2.Level, sendPacket: false);
				if (item != null)
				{
					weaponParts.Add(item);
				}
			}
		}
		foreach (SupportCardExcel config3 in GameData.SupportCardData)
		{
			if (!ItemSanitizer.IsBanned(config3.Genre, config3.Detail, config3.Particular, config3.Level))
			{
				uint targetLevel = ((config3.MaxLevel > 1) ? config3.MaxLevel : 10u);
				GameSupportCardInfo existing = player.InventoryManager.GetSupportCardItemGDPL(ItemTypeEnum.TYPE_SUPPORT, config3.Detail, config3.Particular, config3.Level);
				if (existing == null)
				{
					existing = await player.InventoryManager.AddSupportCardItem(config3.Detail, config3.Particular, config3.Level, targetLevel, sendPacket: false);
				}
				else
				{
					existing.Level = targetLevel;
				}
				if (existing != null)
				{
					supports.Add(existing);
				}
			}
		}
		foreach (CardSkinExcel skin in GameData.CardSkinData.Values)
		{
			if (ItemSanitizer.IsBanned(skin.Genre, skin.Detail, skin.Particular, skin.Level))
			{
				continue;
			}
			ItemTypeEnum type3 = (ItemTypeEnum)skin.Genre;
			if (!HasTemplate(player, type3, skin.Detail, skin.Particular, skin.Level))
			{
				GameSkinInfo item2 = await player.InventoryManager.AddSkinItem(type3, skin.Detail, skin.Particular, skin.Level, sendPacket: false);
				if (item2 != null)
				{
					cosmetics.Add(item2);
				}
			}
		}
		foreach (CardSkinPartsExcel part in GameData.CardSkinPartsData.Values)
		{
			if (ItemSanitizer.IsBanned(part.Genre, part.Detail, part.Particular, part.Level))
			{
				continue;
			}
			ItemTypeEnum type4 = (ItemTypeEnum)part.Genre;
			if (!HasTemplate(player, type4, part.Detail, part.Particular, part.Level))
			{
				BaseGameItemInfo item3 = await player.InventoryManager.AddSkinPartItem(type4, part.Detail, part.Particular, part.Level, sendPacket: false);
				if (item3 != null)
				{
					cosmetics.Add(item3);
				}
			}
		}
		foreach (WeaponSkinExcel skin2 in GameData.WeaponSkinData.Values)
		{
			if (ItemSanitizer.IsBanned(skin2.Genre, skin2.Detail, skin2.Particular, skin2.Level))
			{
				continue;
			}
			ItemTypeEnum type5 = (ItemTypeEnum)skin2.Genre;
			if (!HasTemplate(player, type5, skin2.Detail, skin2.Particular, skin2.Level))
			{
				BaseGameItemInfo item4 = await player.InventoryManager.AddWeaponSkinItem(type5, skin2.Detail, skin2.Particular, skin2.Level, sendPacket: false);
				if (item4 != null)
				{
					cosmetics.Add(item4);
				}
			}
		}
		foreach (ManifestationExcel manifestation in GameData.ManifestationData.Values)
		{
			if (ItemSanitizer.IsBanned(manifestation.Genre, manifestation.Detail, manifestation.Particular, manifestation.Level))
			{
				continue;
			}
			ItemTypeEnum type6 = (ItemTypeEnum)manifestation.Genre;
			if (!HasTemplate(player, type6, manifestation.Detail, manifestation.Particular, manifestation.Level))
			{
				BaseGameItemInfo item5 = await player.InventoryManager.AddManifestationItem(type6, manifestation.Detail, manifestation.Particular, manifestation.Level, sendPacket: false);
				if (item5 != null)
				{
					cosmetics.Add(item5);
				}
			}
		}
		ItemSanitizer.Cleanup(player);
		await ProfileUnlock.Ensure(player, sendPacket: false);
		RikiHandbookService.Ensure(player, sync);
		RikiHandbookService.UnlockForceTypes(player, sync);
		TaskProgressService.Ensure(player);
		DailyPointService.Recompute(player, sync);
		foreach (PlayerAttr attr in player.Data.Attrs.Where((PlayerAttr x) =>
		{
			uint gid = x.Gid;
			return (gid == 2 || gid == 22) ? true : false;
		}))
		{
			player.Attributes.SyncTo(sync, attr);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		if (characters.Count > 0)
		{
			await connection.SendPacket(new PacketNtfCallScript(characters), 0);
		}
		if (weapons.Count > 0)
		{
			await connection.SendPacket(new PacketNtfCallScript(weapons), 0);
		}
		if (supports.Count > 0)
		{
			await connection.SendPacket(new PacketNtfCallScript(supports), 0);
		}
		if (weaponParts.Count > 0)
		{
			await connection.SendPacket(new PacketNtfCallScript(weaponParts), 0);
		}
		if (cosmetics.Count > 0)
		{
			await connection.SendPacket(new PacketNtfCallScript(cosmetics), 0);
		}
		await connection.SendPacket(1004, sync, 0);
	}

	private static bool HasTemplate(PlayerInstance player, ItemTypeEnum type, uint detail, uint particular, uint level)
	{
		ulong template = GameResourceTemplateId.FromGdpl((uint)type, detail, particular, level);
		return player.InventoryManager.InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == template) || player.InventoryManager.InventoryData.Skins.Values.Any((GameSkinInfo x) => x.TemplateId == template) || player.InventoryManager.InventoryData.Weapons.Values.Any((GameWeaponInfo x) => x.TemplateId == template);
	}
}
