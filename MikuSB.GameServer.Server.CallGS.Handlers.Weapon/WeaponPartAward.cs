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

	// 第 581 节（2026-11-27）：配件位的解锁等级是「武器 50 级」——客户端配件页写的就是
	// 「50级解锁」。break_level_limit 里 Break0=20 / Break1=30 / Break2=40 / Break3=50 /
	// Break4=60 / Break5=70 / Break6=80，所以 50 对应的是 Break3，不是 Break1。
	// 原来这里传 1（=30 级），和界面文案对不上：30 级就判「可解锁」。
	public const uint AwardUnlockBreak = 3u;

	public static uint GetAwardUnlockLevel(WeaponUpgradeConfig config, int breakLimitId)
	{
		return config.GetWeaponMaxLevel(breakLimitId, AwardUnlockBreak);
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

	/// <summary>
	/// 第 581 节（2026-11-27）：单把武器的配件位解锁。
	///
	/// 原版客户端在武器升到「1 突满级」（通常 50 级）之后才允许打开配件槽，
	/// 判定依据是属性 Gid=112 / Sid=award[1]award[2] 的第 0 位。
	/// 但服务端从来没有在升级路径上写过这一位，只有客户端主动调
	/// Weapon_GetUpgradePartUnlock 才会补 —— 玩家用 GM 一键拉满或直接领的武器
	/// 就永远停在「50 级解锁」，配件页全是灰的。
	/// 这里给「升级 / 一键满级」用：达到解锁等级就补位，随本次同步下发给客户端。
	/// </summary>
	public static bool RepairWeaponUnlock(PlayerInstance player, GameWeaponInfo weapon, NtfSyncPlayer? sync)
	{
		if (player == null || weapon == null)
		{
			return false;
		}
		WeaponUpgradeConfig config = WeaponUpgradeConfig.Load();
		if (!config.TryGetWeaponTemplate(weapon.TemplateId, out var template))
		{
			return false;
		}
		uint unlockLevel = GetAwardUnlockLevel(config, template.BreakLimitId);
		if (unlockLevel == 0 || weapon.Level < unlockLevel)
		{
			return false;
		}
		if (sync == null)
		{
			return MarkUnlocked(player, weapon.TemplateId, new NtfSyncPlayer());
		}
		return MarkUnlocked(player, weapon.TemplateId, sync);
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
