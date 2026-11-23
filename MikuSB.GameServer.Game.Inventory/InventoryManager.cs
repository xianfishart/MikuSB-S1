using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.GameServer.Server.Packet.Send.Misc;

namespace MikuSB.GameServer.Game.Inventory;

public class InventoryManager(PlayerInstance player) : BasePlayerManager(player)
{
	public InventoryData InventoryData { get; } = DatabaseHelper.GetInstanceOrCreateNew<InventoryData>(player.Uid);

	public async ValueTask<GameWeaponInfo?> AddWeaponItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, uint weaponLevel = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_WEAPON)
		{
			return null;
		}
		WeaponExcel weaponData = GameData.WeaponData.Values.FirstOrDefault((WeaponExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (weaponData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		GameWeaponInfo weaponInfo = new GameWeaponInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			Level = weaponLevel,
			Break = GetWeaponBreak(weaponLevel),
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Weapons[weaponInfo.UniqueId] = weaponInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<GameWeaponInfo>(1) { weaponInfo }));
		}
		return weaponInfo;
	}

	private static uint GetWeaponBreak(uint level)
	{
		switch (level)
		{
		case 0u:
		case 1u:
		case 2u:
		case 3u:
		case 4u:
		case 5u:
		case 6u:
		case 7u:
		case 8u:
		case 9u:
		case 10u:
		case 11u:
		case 12u:
		case 13u:
		case 14u:
		case 15u:
		case 16u:
		case 17u:
		case 18u:
		case 19u:
		case 20u:
			return 1u;
		case 21u:
		case 22u:
		case 23u:
		case 24u:
		case 25u:
		case 26u:
		case 27u:
		case 28u:
		case 29u:
		case 30u:
		case 31u:
		case 32u:
		case 33u:
		case 34u:
		case 35u:
		case 36u:
		case 37u:
		case 38u:
		case 39u:
		case 40u:
			return 2u;
		case 41u:
		case 42u:
		case 43u:
		case 44u:
		case 45u:
		case 46u:
		case 47u:
		case 48u:
		case 49u:
		case 50u:
		case 51u:
		case 52u:
		case 53u:
		case 54u:
		case 55u:
		case 56u:
		case 57u:
		case 58u:
		case 59u:
		case 60u:
			return 3u;
		case 61u:
		case 62u:
		case 63u:
		case 64u:
		case 65u:
		case 66u:
		case 67u:
		case 68u:
		case 69u:
		case 70u:
			return 4u;
		case 71u:
		case 72u:
		case 73u:
		case 74u:
		case 75u:
		case 76u:
		case 77u:
		case 78u:
		case 79u:
		case 80u:
			return 5u;
		case 81u:
		case 82u:
		case 83u:
		case 84u:
		case 85u:
		case 86u:
		case 87u:
		case 88u:
		case 89u:
		case 90u:
			return 6u;
		default:
			return 7u;
		}
	}

	public GameWeaponInfo? GetWeaponItem(uint uniqueId)
	{
		return InventoryData.Weapons.GetValueOrDefault(uniqueId);
	}

	public GameWeaponInfo? GetWeaponItemByTemplateId(ulong templateId)
	{
		return InventoryData.Weapons.Values.FirstOrDefault((GameWeaponInfo x) => x.TemplateId == templateId);
	}

	public GameWeaponInfo? GetWeaponItemGDPL(ItemTypeEnum genre, uint detail, uint particular, uint level)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		return InventoryData.Weapons.Values.FirstOrDefault((GameWeaponInfo x) => x.TemplateId == templateId);
	}

	public async ValueTask<GameSkinInfo?> AddSkinItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_CARD_SKIN)
		{
			return null;
		}
		CardSkinExcel skinData = GameData.CardSkinData.Values.FirstOrDefault((CardSkinExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (skinData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Skins.Values.Any((GameSkinInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		GameSkinInfo skinInfo = new GameSkinInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Skins[skinInfo.UniqueId] = skinInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<GameSkinInfo>(1) { skinInfo }));
		}
		return skinInfo;
	}

	public GameSkinInfo? GetSkinItem(uint uniqueId)
	{
		return InventoryData.Skins.GetValueOrDefault(uniqueId);
	}

	public GameSkinInfo? GetSkinItemByTemplateId(ulong templateId)
	{
		return InventoryData.Skins.Values.FirstOrDefault((GameSkinInfo x) => x.TemplateId == templateId);
	}

	public GameSkinInfo? GetSkinItemGDPL(ItemTypeEnum genre, uint detail, uint particular, uint level)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		return InventoryData.Skins.Values.FirstOrDefault((GameSkinInfo x) => x.TemplateId == templateId);
	}

	public async ValueTask<BaseGameItemInfo?> AddArItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_AR)
		{
			return null;
		}
		ArItemExcel arData = GameData.ArItemData.Values.FirstOrDefault((ArItemExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (arData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo arInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[arInfo.UniqueId] = arInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { arInfo }));
		}
		return arInfo;
	}

	public async ValueTask<GameSupportCardInfo?> AddSupportCardItem(uint detail, uint particular, uint level = 1u, uint cardLevel = 1u, bool sendPacket = true)
	{
		SupportCardExcel spCard = GameData.SupportCardData.FirstOrDefault((SupportCardExcel x) => x.Genre == 3 && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (spCard == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl(3u, detail, particular, level);
		cardLevel = Math.Clamp(cardLevel, 1u, spCard.MaxLevel);
		GameSupportCardInfo info = new GameSupportCardInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = ItemTypeEnum.TYPE_SUPPORT,
			ItemCount = 1u,
			Level = cardLevel,
			AffixId = 0u
		};
		int affixCount = ((cardLevel >= spCard.MaxLevel) ? spCard.TotalAffixCount : spCard.InitialAffixCount);
		for (int i = 0; i < affixCount && i < spCard.AffixPool.Count; i++)
		{
			var (affixId, tier) = SupportAffixService.GenerateRandomAffix(spCard.AffixPool[i]);
			if (affixId != 0)
			{
				SupportAffixStateService.SetAffix(info, i + 1, affixId, tier);
			}
		}
		InventoryData.SupportCards[info.UniqueId] = info;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<GameSupportCardInfo>(1) { info }));
		}
		return info;
	}

	public GameSupportCardInfo? GetSupportCardItem(uint uniqueId)
	{
		return InventoryData.SupportCards.GetValueOrDefault(uniqueId);
	}

	public GameSupportCardInfo? GetSupportCardByTemplateId(ulong templateId)
	{
		return InventoryData.SupportCards.Values.FirstOrDefault((GameSupportCardInfo x) => x.TemplateId == templateId);
	}

	public GameSupportCardInfo? GetSupportCardItemGDPL(ItemTypeEnum genre, uint detail, uint particular, uint level)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		return InventoryData.SupportCards.Values.FirstOrDefault((GameSupportCardInfo x) => x.TemplateId == templateId);
	}

	public async ValueTask<BaseGameItemInfo?> AddManifestationItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_MANIFESTATION)
		{
			return null;
		}
		ManifestationExcel manifestData = GameData.ManifestationData.Values.FirstOrDefault((ManifestationExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (manifestData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo manifestInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[manifestInfo.UniqueId] = manifestInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { manifestInfo }));
		}
		return manifestInfo;
	}

	public BaseGameItemInfo? GetNormalItem(uint uniqueId)
	{
		return InventoryData.Items.GetValueOrDefault(uniqueId);
	}

	public BaseGameItemInfo? GetNormalItemByTemplateId(ulong templateId)
	{
		return InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
	}

	public BaseGameItemInfo? GetNormalItemGDPL(ItemTypeEnum genre, uint detail, uint particular, uint level)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		return InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
	}

	/// <summary>
	/// 把背包里的怪物卡统一换成 <see cref="MonsterCardItemInfo"/>（带 Affixs）。
	/// 读库后所有条目都是裸 BaseGameItemInfo（子类字段不落库），登录全量同步前补一遍，
	/// 客户端伙伴详情才有属性档位（2026-11-15 第 340 节）。
	/// </summary>
	public void EnsureMonsterCardInfos()
	{
		int uid = Player.Uid;
		List<uint> pending = null;
		foreach (KeyValuePair<uint, BaseGameItemInfo> kv in InventoryData.Items)
		{
			if (kv.Value.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
			{
				continue;
			}
			if (kv.Value is MonsterCardItemInfo exist)
			{
				exist.EnsureAffixs();
				// 技能升阶的等级存在 vircapture_card_skill 表里，读库后盖回 Affixs（第 342 节）。
				VirCaptureCardSkillStore.Apply(uid, exist);
				continue;
			}
			(pending ??= new List<uint>()).Add(kv.Key);
		}
		if (pending == null)
		{
			return;
		}
		foreach (uint cardUid in pending)
		{
			BaseGameItemInfo old = InventoryData.Items[cardUid];
			MonsterCardItemInfo card = new MonsterCardItemInfo
			{
				UniqueId = old.UniqueId,
				TemplateId = old.TemplateId,
				ItemCount = old.ItemCount,
				ItemType = old.ItemType,
				Flag = old.Flag,
				Level = old.Level,
				Exp = old.Exp
			};
			card.EnsureAffixs();
			VirCaptureCardSkillStore.Apply(uid, card);
			InventoryData.Items[cardUid] = card;
		}
	}

	public async ValueTask<BaseGameItemInfo?> AddMonsterCardItem(uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl(18u, detail, particular, level);
		if (!GameData.MonsterCardData.ContainsKey(templateId))
		{
			return null;
		}
		// 用带 Affixs 的子类，客户端伙伴详情才能显示攻击/防御/生命的 A/B/C/S 档位。
		MonsterCardItemInfo monsterInfo = new MonsterCardItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = ItemTypeEnum.TYPE_MONSTER_CARD,
			ItemCount = 1u
		};
		monsterInfo.EnsureAffixs();
		InventoryData.Items[monsterInfo.UniqueId] = monsterInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { monsterInfo }));
		}
		return monsterInfo;
	}

	private static uint GetSuppliesMaxCount(SuppliesExcel suppliesData)
	{
		return (suppliesData.Genre == 5 && suppliesData.Detail == 4) ? 999999u : 99999u;
	}

	public async ValueTask<BaseGameItemInfo?> AddSuppliesItem(SuppliesExcel suppliesData, uint count, bool sendPacket = true)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl(suppliesData.Genre, suppliesData.Detail, suppliesData.Particular, suppliesData.Level);
		uint maxCount = GetSuppliesMaxCount(suppliesData);
		uint giveCount = Math.Min(count, maxCount);
		BaseGameItemInfo existing = InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
		if (existing != null)
		{
			existing.ItemCount = Math.Min(existing.ItemCount + giveCount, maxCount);
			// 已有条目也必须推同步：客户端按 UniqueId 覆盖 ItemCount，
			// 以前这里直接 return，导致 GM 发代币时服务端数量涨了、客户端一动不动
			// （2026-11-12 修复记录第 237 节）。其它调用方一律 sendPacket:false，
			// 由上层自己打包，不受影响。
			if (sendPacket)
			{
				await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { existing }));
			}
			return existing;
		}
		BaseGameItemInfo itemInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = ItemTypeEnum.TYPE_SUPPLIES,
			ItemCount = giveCount
		};
		while (InventoryData.Items.ContainsKey(itemInfo.UniqueId))
		{
			itemInfo.UniqueId = InventoryData.NextUniqueUid++;
		}
		InventoryData.Items[itemInfo.UniqueId] = itemInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { itemInfo }));
		}
		return itemInfo;
	}

	public async ValueTask<BaseGameItemInfo?> AddWeaponSkinItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_WEAPON_SKIN)
		{
			return null;
		}
		WeaponSkinExcel skinData = GameData.WeaponSkinData.Values.FirstOrDefault((WeaponSkinExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (skinData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo skinInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[skinInfo.UniqueId] = skinInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { skinInfo }));
		}
		return skinInfo;
	}

	public async ValueTask<BaseGameItemInfo?> AddProfileItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre < ItemTypeEnum.TYPE_PROFILE || genre > ItemTypeEnum.TYPE_ANALYST)
		{
			return null;
		}
		ProfileExcel profileData = GameData.ProfileData.Values.FirstOrDefault((ProfileExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (profileData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo profileInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[profileInfo.UniqueId] = profileInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { profileInfo }));
		}
		return profileInfo;
	}

	public async ValueTask<BaseGameItemInfo?> AddSkinPartItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_CARD_SKIN_PART)
		{
			return null;
		}
		CardSkinPartsExcel skinPartData = GameData.CardSkinPartsData.Values.FirstOrDefault((CardSkinPartsExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (skinPartData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo skinPartInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[skinPartInfo.UniqueId] = skinPartInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { skinPartInfo }));
		}
		return skinPartInfo;
	}

	public async ValueTask<BaseGameItemInfo?> AddCallItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_CALL)
		{
			return null;
		}
		CallItemExcel callData = GameData.CallItemData.Values.FirstOrDefault((CallItemExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (callData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo callInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[callInfo.UniqueId] = callInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { callInfo }));
		}
		return callInfo;
	}

	public async ValueTask<BaseGameItemInfo?> AddWeaponPartItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_WEAPON_PART)
		{
			return null;
		}
		WeaponPartsExcel weaponPartData = GameData.WeaponPartsData.Values.FirstOrDefault((WeaponPartsExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (weaponPartData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo weaponPartInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[weaponPartInfo.UniqueId] = weaponPartInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { weaponPartInfo }));
		}
		return weaponPartInfo;
	}

	public async ValueTask<BaseGameItemInfo?> AddHouseFurnitureItem(ItemTypeEnum genre, uint detail, uint particular, uint level = 1u, bool sendPacket = true)
	{
		if (genre != ItemTypeEnum.TYPE_HOUSE)
		{
			return null;
		}
		DormGiftExcel houseFurnitureData = GameData.DormGiftData.Values.FirstOrDefault((DormGiftExcel x) => (ulong)x.Genre == (ulong)genre && x.Detail == detail && x.Particular == particular && x.Level == level);
		if (houseFurnitureData == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl((uint)genre, detail, particular, level);
		if (InventoryData.Items.Values.Any((BaseGameItemInfo x) => x.TemplateId == templateId))
		{
			return null;
		}
		BaseGameItemInfo furnitureInfo = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = InventoryData.NextUniqueUid++,
			ItemType = genre,
			ItemCount = 1u
		};
		InventoryData.Items[furnitureInfo.UniqueId] = furnitureInfo;
		if (sendPacket)
		{
			await Player.SendPacket(new PacketNtfCallScript(new List<BaseGameItemInfo>(1) { furnitureInfo }));
		}
		return furnitureInfo;
	}
}
