using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

internal static class WeaponPartAward
{
	private const uint LockGid = 112u;

	private const uint FieldMask = 31u;

	private const int UnlockShift = 0;

	private const int ClaimedShift = 5;

	private const uint UnlockValue = 1u;

	private const uint ClaimedValue = 1u;

	public const uint AwardUnlockBreak = 1u;

	public static uint GetAwardUnlockLevel(WeaponUpgradeConfig config, int breakLimitId)
	{
		return config.GetWeaponMaxLevel(breakLimitId, 1u);
	}

	public static List<int>? GetAward(ulong weaponTemplateId)
	{
		List<int> list = GameData.WeaponData.Values.FirstOrDefault((WeaponExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == weaponTemplateId)?.WeaponPartsAward;
		if (list == null || list.Count < 4)
		{
			return null;
		}
		return (list[0] > 0 && list[1] > 0 && list[2] > 0 && list[3] > 0) ? list : null;
	}

	public static uint GetSid(List<int> award)
	{
		return uint.Parse($"{award[1]}{award[2]}");
	}

	public static bool IsUnlocked(PlayerInstance player, uint sid)
	{
		return (player.Attributes.GetValue(112u, sid) & 0x1F) == 1;
	}

	public static bool IsClaimed(PlayerInstance player, uint sid)
	{
		return ((player.Attributes.GetValue(112u, sid) >> 5) & 0x1F) == 1;
	}

	public static bool MarkUnlocked(PlayerInstance player, ulong weaponTemplateId, NtfSyncPlayer sync)
	{
		List<int> award = GetAward(weaponTemplateId);
		return award != null && MarkUnlocked(player, GetSid(award), sync);
	}

	public static bool MarkUnlocked(PlayerInstance player, uint sid, NtfSyncPlayer sync)
	{
		uint value = player.Attributes.GetValue(112u, sid);
		if ((value & 0x1F) == 1)
		{
			return false;
		}
		value = (value & 0xFFFFFFE0u) | 1;
		player.Attributes.Set(112u, sid, value);
		player.Attributes.SyncTo(sync, 112u, sid, value);
		return true;
	}

	public static bool MarkClaimed(PlayerInstance player, uint sid, NtfSyncPlayer sync)
	{
		uint value = player.Attributes.GetValue(112u, sid);
		if (((value >> 5) & 0x1F) == 1)
		{
			return false;
		}
		value = (value & 0xFFFFFC1Fu) | 0x20;
		player.Attributes.Set(112u, sid, value);
		player.Attributes.SyncTo(sync, 112u, sid, value);
		return true;
	}

	public static bool RepairUnlocked(PlayerInstance player)
	{
		WeaponUpgradeConfig weaponUpgradeConfig = WeaponUpgradeConfig.Load();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		bool flag = false;
		foreach (GameWeaponInfo value in player.InventoryManager.InventoryData.Weapons.Values)
		{
			if (weaponUpgradeConfig.TryGetWeaponTemplate(value.TemplateId, out var template))
			{
				uint awardUnlockLevel = GetAwardUnlockLevel(weaponUpgradeConfig, template.BreakLimitId);
				if (awardUnlockLevel != 0 && value.Level >= awardUnlockLevel)
				{
					flag |= MarkUnlocked(player, value.TemplateId, sync);
				}
			}
		}
		return flag;
	}
}
