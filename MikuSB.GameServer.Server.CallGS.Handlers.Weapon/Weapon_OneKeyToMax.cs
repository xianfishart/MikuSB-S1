using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_OneKeyToMax")]
public class Weapon_OneKeyToMax : ICallGSHandler
{
	private const uint MaxBreak = 6u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		OneKeyToMaxParam req = JsonSerializer.Deserialize<OneKeyToMaxParam>(param);
		if (req == null || req.Id <= 0 || req.TbBreakUpgradeMat == null || req.TbBreakUpgradeMat.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		GameWeaponInfo weapon = player.InventoryManager.GetWeaponItem((uint)req.Id);
		if (weapon == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		WeaponUpgradeConfig config = WeaponUpgradeConfig.Load();
		if (!config.TryGetWeaponTemplate(weapon.TemplateId, out var targetTemplate))
		{
			await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		InventoryData inventory = player.InventoryManager.InventoryData;
		HashSet<uint> equippedWeaponIds = (from x in player.CharacterManager.CharacterData.Characters
			select x.WeaponUniqueId into x
			where x != 0
			select x).ToHashSet();
		foreach (List<JsonElement> stage in req.TbBreakUpgradeMat)
		{
			if (stage == null || stage.Count < 3)
			{
				continue;
			}
			List<List<int>> matList = stage[1].Deserialize<List<List<int>>>();
			if (matList == null)
			{
				continue;
			}
			foreach (List<int> entry in matList)
			{
				if (entry == null || entry.Count < 2)
				{
					continue;
				}
				uint itemId = (uint)Math.Max(0, entry[0]);
				uint count = (uint)Math.Max(0, entry[1]);
				if (itemId != 0 && count != 0)
				{
					if (itemId == weapon.UniqueId)
					{
						await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"sErr\":\"tip.material_not_enough\"}");
						return;
					}
					BaseGameItemInfo material = FindInventoryItem(inventory, itemId);
					if (material == null || material.ItemCount < count)
					{
						await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"sErr\":\"tip.material_not_enough\"}");
						return;
					}
					if (material is GameWeaponInfo materialWeapon && (materialWeapon.EquipAvatarId != 0 || equippedWeaponIds.Contains(materialWeapon.UniqueId)))
					{
						await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"sErr\":\"tip.material_not_enough\"}");
						return;
					}
				}
			}
		}
		List<Item> syncItems = new List<Item>();
		uint weaponLevel = ((weapon.Level == 0) ? 1u : weapon.Level);
		weapon.Level = weaponLevel;
		foreach (List<JsonElement> stage2 in req.TbBreakUpgradeMat)
		{
			if (stage2 == null || stage2.Count < 3)
			{
				continue;
			}
			List<List<int>> matList2 = stage2[1].Deserialize<List<List<int>>>();
			bool doBreak = stage2[2].GetInt32() == 1;
			uint key;
			if (matList2 != null && matList2.Count > 0)
			{
				Dictionary<uint, uint> materialsUsed = new Dictionary<uint, uint>();
				foreach (List<int> entry2 in matList2)
				{
					if (entry2 != null && entry2.Count >= 2)
					{
						uint itemId2 = (uint)Math.Max(0, entry2[0]);
						uint count2 = (uint)Math.Max(0, entry2[1]);
						if (itemId2 != 0 && count2 != 0)
						{
							materialsUsed[itemId2] = materialsUsed.GetValueOrDefault(itemId2) + count2;
						}
					}
				}
				ulong totalExp = 0uL;
				uint value;
				foreach (KeyValuePair<uint, uint> item in materialsUsed)
				{
					item.Deconstruct(out key, out value);
					uint itemId3 = key;
					uint count3 = value;
					BaseGameItemInfo material2 = FindInventoryItem(inventory, itemId3);
					if (config.TryGetMaterialGain(material2, out var gainExp))
					{
						totalExp += gainExp * count3;
					}
				}
				foreach (KeyValuePair<uint, uint> item2 in materialsUsed)
				{
					item2.Deconstruct(out value, out key);
					uint itemId4 = value;
					uint count4 = key;
					BaseGameItemInfo material3 = FindInventoryItem(inventory, itemId4);
					material3.ItemCount -= count4;
					if (material3.ItemCount == 0)
					{
						RemoveInventoryItem(inventory, itemId4);
						Item proto = material3.ToProto();
						proto.Count = 0u;
						syncItems.Add(proto);
					}
					else
					{
						syncItems.Add(material3.ToProto());
					}
				}
				if (totalExp != 0)
				{
					(uint, uint) tuple = config.ApplyWeaponExp(maxLevel: config.GetWeaponMaxLevel(targetTemplate.BreakLimitId, weapon.Break), level: weapon.Level, exp: weapon.Exp, addExp: totalExp, color: targetTemplate.Color);
					uint newLevel = tuple.Item1;
					uint newExp = tuple.Item2;
					weapon.Level = newLevel;
					weapon.Exp = newExp;
				}
			}
			if (doBreak && weapon.Break < 6)
			{
				key = weapon.Break++;
			}
		}
		syncItems.Add(weapon.ToProto());
		DatabaseHelper.SaveDatabaseType(inventory);
		uint finalMaxLevel = config.GetWeaponMaxLevel(targetTemplate.BreakLimitId, weapon.Break);
		bool bMaxUnlock = finalMaxLevel != 0 && weapon.Level >= finalMaxLevel;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		await CallGSRouter.SendScript(connection, "Weapon_OneKeyToMax", "{\"bMaxUnLock\":" + (bMaxUnlock ? "true" : "false") + "}", sync);
	}

	private static BaseGameItemInfo? FindInventoryItem(InventoryData inventory, uint itemId)
	{
		if (inventory.Weapons.TryGetValue(itemId, out GameWeaponInfo value))
		{
			return value;
		}
		if (inventory.Skins.TryGetValue(itemId, out GameSkinInfo value2))
		{
			return value2;
		}
		if (inventory.Items.TryGetValue(itemId, out BaseGameItemInfo value3))
		{
			return value3;
		}
		return null;
	}

	private static void RemoveInventoryItem(InventoryData inventory, uint itemId)
	{
		inventory.Weapons.Remove(itemId);
		inventory.Skins.Remove(itemId);
		inventory.Items.Remove(itemId);
	}
}
