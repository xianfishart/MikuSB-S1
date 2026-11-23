using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Lineup;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Game.Character;

public class CharacterManager(PlayerInstance player) : BasePlayerManager(player)
{
	public CharacterData CharacterData { get; } = DatabaseHelper.GetInstanceOrCreateNew<CharacterData>(player.Uid);

	public async ValueTask<CharacterInfo?> AddCharacter(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, uint star = 1u, bool sendPacket = true)
	{
		ulong characterId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, 1u);
		if (CharacterData.Characters.Any((CharacterInfo a) => a.TemplateId == characterId))
		{
			return null;
		}
		CardExcel CharacterExcel = GameData.CardData.Values.FirstOrDefault((CardExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular);
		if (CharacterExcel == null)
		{
			return null;
		}
		CharacterInfo character = new CharacterInfo
		{
			Guid = CharacterData.NextCharacterGuid++,
			TemplateId = characterId,
			Level = level,
			Exp = 0,
			Break = star,
			Timestamp = Extensions.GetUnixSec()
		};
		GameWeaponInfo weaponInfo = await Player.InventoryManager.AddWeaponItem((ItemTypeEnum)CharacterExcel.DefaultWeaponGPDL[0], CharacterExcel.DefaultWeaponGPDL[1], CharacterExcel.DefaultWeaponGPDL[2], CharacterExcel.DefaultWeaponGPDL[3]);
		if (weaponInfo != null)
		{
			character.WeaponUniqueId = weaponInfo.UniqueId;
		}
		GameSkinInfo skinItemGDPL = Player.InventoryManager.GetSkinItemGDPL(ItemTypeEnum.TYPE_CARD_SKIN, detail, particular, level);
		GameSkinInfo gameSkinInfo = skinItemGDPL;
		if (gameSkinInfo == null)
		{
			gameSkinInfo = await Player.InventoryManager.AddSkinItem(ItemTypeEnum.TYPE_CARD_SKIN, detail, particular, level);
		}
		GameSkinInfo skinInfo = gameSkinInfo;
		if (skinInfo != null)
		{
			character.SkinId = skinInfo.UniqueId;
		}
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<CharacterInfo>(1) { character }));
		}
		CharacterData.Characters.Add(character);
		return character;
	}

	public CharacterInfo? GetCharacter(ulong TemplateId)
	{
		return CharacterData.Characters.Find((CharacterInfo Character) => Character.TemplateId == TemplateId);
	}

	public CharacterInfo? GetCharacterByGUID(uint guid)
	{
		return CharacterData.Characters.Find((CharacterInfo Character) => Character.Guid == guid);
	}

	public CharacterInfo? GetCharacterGDPL(ItemTypeEnum genre, int detail, int particular)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, (uint)detail, (uint)particular, 1u);
		return CharacterData.Characters.Find((CharacterInfo Character) => Character.TemplateId == templateId);
	}

	public void AddExperienceToLineup(uint amount, NtfSyncPlayer sync, uint lineupId = 0u)
	{
		if (amount == 0 || lineupId == 0 || !Player.LineupManager.LineupData.LineupInfo.TryGetValue((int)lineupId, out LineupDataInfo value))
		{
			return;
		}
		IEnumerable<uint> enumerable = new uint[3] { value.Member1, value.Member2, value.Member3 }.Where((uint x) => x != 0).Distinct();
		foreach (uint item in enumerable)
		{
			CharacterInfo character = GetCharacterByGUID(item);
			if (character == null)
			{
				continue;
			}
			CardExcel cardExcel = GameData.CardData.Values.FirstOrDefault((CardExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == character.TemplateId);
			if (cardExcel == null)
			{
				continue;
			}
			ulong num = (ulong)(Math.Max(0, character.Exp) + amount);
			UpgradeExpExcel value2;
			while (character.Level < 80 && GameData.UpgradeExpData.TryGetValue((int)character.Level, out value2))
			{
				uint num2 = ((cardExcel.Color >= 5) ? value2.SSRCardNeedExp : value2.CardNeedExp);
				if (num2 == 0 || num < num2)
				{
					break;
				}
				num -= num2;
				character.Level++;
			}
			character.Exp = (int)Math.Min(2147483647uL, num);
			sync.Items.Add(character.ToProto());
		}
	}

	public int AddExperienceToAllLineups(uint amount, NtfSyncPlayer sync)
	{
		if (amount == 0)
		{
			return 0;
		}
		HashSet<uint> hashSet = new HashSet<uint>();
		foreach (LineupDataInfo value2 in Player.LineupManager.LineupData.LineupInfo.Values)
		{
			if (value2.Member1 != 0)
			{
				hashSet.Add(value2.Member1);
			}
			if (value2.Member2 != 0)
			{
				hashSet.Add(value2.Member2);
			}
			if (value2.Member3 != 0)
			{
				hashSet.Add(value2.Member3);
			}
		}
		int num = 0;
		foreach (uint item in hashSet)
		{
			CharacterInfo characterByGUID = GetCharacterByGUID(item);
			if (characterByGUID != null && characterByGUID.Level < 80)
			{
				int num2 = characterByGUID.Exp + (int)amount;
				UpgradeExpExcel value;
				while (characterByGUID.Level < 80 && GameData.UpgradeExpData.TryGetValue((int)characterByGUID.Level, out value) && value.CardNeedExp != 0 && num2 >= value.CardNeedExp)
				{
					num2 -= (int)value.CardNeedExp;
					characterByGUID.Level++;
					num++;
				}
				characterByGUID.Exp = num2;
				sync.Items.Add(characterByGUID.ToProto());
			}
		}
		return num;
	}

	public async ValueTask RepairCharacterWeapons()
	{
		bool changed = false;
		HashSet<uint> equippedWeaponIds = new HashSet<uint>();
		foreach (CharacterInfo character in CharacterData.Characters)
		{
			GameWeaponInfo weapon = Player.InventoryManager.GetWeaponItem(character.WeaponUniqueId);
			if (weapon == null)
			{
				CardExcel cardData = GameData.CardData.Values.FirstOrDefault((CardExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == character.TemplateId);
				if (cardData != null && cardData.DefaultWeaponGPDL.Count >= 4)
				{
					weapon = await Player.InventoryManager.AddWeaponItem((ItemTypeEnum)cardData.DefaultWeaponGPDL[0], cardData.DefaultWeaponGPDL[1], cardData.DefaultWeaponGPDL[2], cardData.DefaultWeaponGPDL[3], 1u, sendPacket: false);
					if (weapon != null)
					{
						character.WeaponUniqueId = weapon.UniqueId;
						changed = true;
					}
				}
			}
			if (weapon != null)
			{
				if (weapon.EquipAvatarId != character.Guid)
				{
					weapon.EquipAvatarId = character.Guid;
					changed = true;
				}
				equippedWeaponIds.Add(weapon.UniqueId);
			}
		}
		foreach (GameWeaponInfo weapon2 in Player.InventoryManager.InventoryData.Weapons.Values)
		{
			if (!equippedWeaponIds.Contains(weapon2.UniqueId) && weapon2.EquipAvatarId != 0)
			{
				weapon2.EquipAvatarId = 0u;
				changed = true;
			}
		}
		if (changed)
		{
			DatabaseHelper.SaveDatabaseType(CharacterData);
			DatabaseHelper.SaveDatabaseType(Player.InventoryManager.InventoryData);
		}
	}
}
