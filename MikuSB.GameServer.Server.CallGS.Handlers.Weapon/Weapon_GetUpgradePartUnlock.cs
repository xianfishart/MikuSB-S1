using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_GetUpgradePartUnlock")]
public class Weapon_GetUpgradePartUnlock : CallGSHandler<WeaponGetUpgradePartUnlockParam>
{
	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, WeaponGetUpgradePartUnlockParam req)
	{
		PlayerInstance player = context.Connection.Player;
		if (req == null || req.WeaponId <= 0 || req.PartGdpl == null || req.PartGdpl.Count < 4)
		{
			return CallGSResult.Error("error.BadParam");
		}
		GameWeaponInfo weapon = player.InventoryManager.GetWeaponItem((uint)req.WeaponId);
		if (weapon == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		List<int> award = WeaponPartAward.GetAward(weapon.TemplateId);
		if (award == null || !award.Take(4).SequenceEqual(req.PartGdpl.Take(4)))
		{
			return CallGSResult.Error("error.BadParam");
		}
		WeaponUpgradeConfig config = WeaponUpgradeConfig.Load();
		if (!config.TryGetWeaponTemplate(weapon.TemplateId, out var template))
		{
			return CallGSResult.Error("error.BadParam");
		}
		uint unlockLevel = WeaponPartAward.GetAwardUnlockLevel(config, template.BreakLimitId);
		if (unlockLevel == 0 || weapon.Level < unlockLevel)
		{
			return CallGSResult.Error("error.BadParam");
		}
		uint sid = WeaponPartAward.GetSid(award);
		if (WeaponPartAward.IsClaimed(player, sid))
		{
			return CallGSResult.Error("error.BadParam");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		WeaponPartAward.MarkUnlocked(player, sid, sync);
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>> { new uint[5]
		{
			(uint)award[0],
			(uint)award[1],
			(uint)award[2],
			(uint)award[3],
			1u
		} };
		await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync).ConfigureAwait(continueOnCapturedContext: false);
		WeaponPartAward.MarkClaimed(player, sid, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		return CallGSResult.Ok("null", sync);
	}
}
