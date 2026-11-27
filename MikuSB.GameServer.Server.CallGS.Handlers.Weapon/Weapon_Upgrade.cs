using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Upgrade")]
public class Weapon_Upgrade : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		WeaponUpgradeParam req = JsonSerializer.Deserialize<WeaponUpgradeParam>(param);
		if (req == null || req.Id <= 0 || req.TbMat == null || req.TbMat.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"tip.material_not_enough\"}");
			return;
		}
		GameWeaponInfo targetWeapon = player.InventoryManager.GetWeaponItem((uint)req.Id);
		if (targetWeapon == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"tip.material_not_enough\"}");
			return;
		}
		WeaponUpgradeConfig config = WeaponUpgradeConfig.Load();
		if (!config.TryGetWeaponTemplate(targetWeapon.TemplateId, out var targetTemplate))
		{
			await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		Dictionary<uint, uint> requestedMaterials = new Dictionary<uint, uint>();
		foreach (List<int> row in req.TbMat)
		{
			if (row != null && row.Count >= 2)
			{
				uint itemId = (uint)Math.Max(0, row[0]);
				uint count = (uint)Math.Max(0, row[1]);
				if (itemId != 0 && count != 0)
				{
					requestedMaterials[itemId] = requestedMaterials.GetValueOrDefault(itemId) + count;
				}
			}
		}
		if (requestedMaterials.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"tip.material_not_enough\"}");
			return;
		}
		ulong totalExp = 0uL;
		List<Item> syncItems = new List<Item>();
		HashSet<uint> equippedWeaponIds = (from x in player.CharacterManager.CharacterData.Characters
			select x.WeaponUniqueId into x
			where x != 0
			select x).ToHashSet();
		uint value;
		uint key;
		foreach (KeyValuePair<uint, uint> item in requestedMaterials)
		{
			item.Deconstruct(out value, out key);
			uint itemId2 = value;
			uint count2 = key;
			if (itemId2 == targetWeapon.UniqueId)
			{
				await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"tip.material_not_enough\"}");
				return;
			}
			BaseGameItemInfo material = FindInventoryItem(player.InventoryManager.InventoryData, itemId2);
			if (material == null || material.ItemCount < count2)
			{
				await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"tip.material_not_enough\"}");
				return;
			}
			if (material is GameWeaponInfo materialWeapon && (materialWeapon.EquipAvatarId != 0 || equippedWeaponIds.Contains(materialWeapon.UniqueId)))
			{
				await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"tip.material_not_enough\"}");
				return;
			}
			if (!TryGetMaterialGain(config, material, out var gainExp))
			{
				await CallGSRouter.SendScript(connection, "Weapon_Upgrade", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			totalExp += gainExp * count2;
		}
		foreach (KeyValuePair<uint, uint> item2 in requestedMaterials)
		{
			item2.Deconstruct(out key, out value);
			uint itemId3 = key;
			uint count3 = value;
			BaseGameItemInfo material2 = FindInventoryItem(player.InventoryManager.InventoryData, itemId3);
			material2.ItemCount -= count3;
			if (material2.ItemCount == 0)
			{
				RemoveInventoryItem(player.InventoryManager.InventoryData, itemId3);
				syncItems.Add(BuildRemovedProto(material2));
			}
			else
			{
				syncItems.Add(material2.ToProto());
			}
		}
		uint maxLevel = config.GetWeaponMaxLevel(targetTemplate.BreakLimitId, targetWeapon.Break);
		uint oldLevel = ((targetWeapon.Level == 0) ? 1u : targetWeapon.Level);
		targetWeapon.Level = oldLevel;
		(uint, uint) tuple = config.ApplyWeaponExp(targetWeapon.Level, targetWeapon.Exp, totalExp, targetTemplate.Color, maxLevel);
		uint newLevel = tuple.Item1;
		uint newExp = tuple.Item2;
		targetWeapon.Level = newLevel;
		targetWeapon.Exp = newExp;
		syncItems.Add(targetWeapon.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		TaskEventService.OnWeaponUpgrade(player);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		TaskEventService.SyncType(player, 31, sync);
		// 第 581 节：跨过「1 突满级」时补上配件解锁位，否则配件页永远显示「N 级解锁」。
		if (WeaponPartAward.RepairWeaponUnlock(player, targetWeapon, sync))
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		// 第 438 节：活动任务「日常训练 = 将任意武器升级 1 次」（Type 38），只有真的升了级才算。
		if (newLevel > oldLevel)
		{
			MikuSB.GameServer.Game.Activity.ActivityQuestService.Add(player, 38, 1, sync);
		}
		bool bMaxUnlock = maxLevel != 0 && targetWeapon.Level >= maxLevel;
		string arg = "{\"bMaxUnLock\":" + (bMaxUnlock ? "true" : "false") + "}";
		await CallGSRouter.SendScript(connection, "Weapon_Upgrade", arg, sync);
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

	private static Item BuildRemovedProto(BaseGameItemInfo item)
	{
		Item item2 = item.ToProto();
		item2.Count = 0u;
		return item2;
	}

	private static bool TryGetMaterialGain(WeaponUpgradeConfig config, BaseGameItemInfo item, out ulong exp)
	{
		exp = 0uL;
		if (config.TryGetWeaponTemplate(item.TemplateId, out var template))
		{
			exp = template.ProvideExp;
			if (item is GameWeaponInfo { Level: >1u } gameWeaponInfo)
			{
				exp += config.GetWeaponRecycleExp(template, gameWeaponInfo.Level);
			}
			return true;
		}
		if (config.TryGetSuppliesTemplate(item.TemplateId, out var template2))
		{
			exp = template2.ProvideExp;
			return true;
		}
		return false;
	}
}
