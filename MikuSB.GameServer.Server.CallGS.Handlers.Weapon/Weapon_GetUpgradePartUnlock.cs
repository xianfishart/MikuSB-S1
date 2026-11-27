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
		// 第 581 节（2026-11-27）：sid 是「配件维度」的键（award[1]+award[2]），
		// 多把武器会共用同一个配件奖励。只要这个配件已经解锁（别的高等级武器
		// 写过解锁位），就允许领取 —— 否则低等级武器界面上明明写着「可领取」，
		// 点下去却被等级校验挡回 error.BadParam，客户端拿到错误对象直接显示成
		// 「table: 0x…」，看起来就像领取按钮坏了。
		uint sid = WeaponPartAward.GetSid(award);
		if (WeaponPartAward.IsClaimed(player, sid))
		{
			return CallGSResult.Error("error.BadParam");
		}
		uint unlockLevel = WeaponPartAward.GetAwardUnlockLevel(config, template.BreakLimitId);
		if (!WeaponPartAward.IsUnlocked(player, sid) && (unlockLevel == 0 || weapon.Level < unlockLevel))
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
		return CallGSResult.Ok("{}", sync);
	}
}
