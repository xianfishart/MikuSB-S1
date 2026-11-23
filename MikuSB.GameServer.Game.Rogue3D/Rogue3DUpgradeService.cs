using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Rogue3D;

/// <summary>
/// 逻辑研习（Rogue3D 科技树）升级。
///
/// 2026-11-14 修复记录第 300 节：
///   原来的 Rogue3D_UpgradeTech 只把等级 +1 就回包，**完全不扣智略点**，
///   于是可以无限白点。消耗口径在 Resources/dlc/rogue3d/server_06_sciencelevel.json：
///   每级一个主键（server_02_science.json 的 LevelList 顺序排列），Cost = [货币ID, 数量]，
///   货币 25 = 智略点（cash.json ROGUE3EXPSCIENCE，属性 Sid = 25*2+1 = 51）。
///
/// 抽出成服务的原因：handler 和 /r3upgrade 命令要走同一段逻辑，
/// 免得实测路径和真实路径不一致（第 299 节 startergift 同理）。
/// </summary>
internal static class Rogue3DUpgradeService
{
	private static readonly Logger Log = new Logger("Rogue3DUpgrade");

	/// <summary>
	/// 升一级。成功时已扣点、已写属性、sync 里已带等级与货币同步；
	/// 失败时 error 为客户端错误码（sErr），其余 out 参数为诊断信息。
	/// </summary>
	internal static bool TryUpgrade(PlayerInstance player, uint techId, NtfSyncPlayer sync, out string error, out uint newLevel, out uint maxLevel, out uint spendType, out uint spendAmount)
	{
		error = null;
		newLevel = 0u;
		maxLevel = 0u;
		spendType = 0u;
		spendAmount = 0u;
		if (!Rogue3DTechHelper.TryGetScience(techId, out Rogue3DScienceExcel science) || science == null)
		{
			error = "error.BadParam";
			return false;
		}
		// 2026-11-14 二次修正：这里原本加了 IsUnlocked(UnlockCondition) 校验，
		// 实测会误伤 —— 例：106「弩·精通」前置 103 已经是 5 级、客户端也能点，
		// 服务端却回 error.condition_limit（客户端弹「不符合限制条件」）。
		// 解锁限制本来就由客户端 UI 负责（未解锁的节点点不了），服务端只认等级与消耗，
		// 因此这里不再重复校验，避免与服务端的属性缓存口径不一致导致误拦。
		maxLevel = ((science.MaxLevel != 0) ? science.MaxLevel : 5u);
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == techId);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 124u,
				Sid = techId
			};
			player.Data.Attrs.Add(attr);
		}
		newLevel = attr.Val;
		if (attr.Val >= maxLevel)
		{
			error = "tip.Rogue3D_TechMax";
			return false;
		}
		// 目标等级行 = LevelList[当前等级]（0 基，当前 0 级时取第 1 项）
		uint nextLevelId = 0u;
		if (science.LevelList != null && (int)attr.Val < science.LevelList.Count)
		{
			nextLevelId = science.LevelList[(int)attr.Val];
		}
		if (nextLevelId != 0 && GameData.Rogue3DScienceLevelData.TryGetValue(nextLevelId, out Rogue3DScienceLevelExcel levelRow) && levelRow != null && levelRow.Cost != null && levelRow.Cost.Count >= 2)
		{
			spendType = levelRow.Cost[0];
			spendAmount = levelRow.Cost[1];
		}
		if (spendType == 0 || spendAmount == 0)
		{
			// 配置缺失时宁可不放行，避免又变成白送等级。
			Log.Warn($"uid={player.Uid} techId={techId} lv{attr.Val}->{attr.Val + 1} levelRow={nextLevelId} cost missing, reject");
			error = "error.BadParam";
			return false;
		}
		PlayerAttr wallet = player.Attributes.GetOrCreate(1u, AttrIds.Currency.GetSid(spendType));
		uint balance = wallet.Val;
		if (balance < spendAmount)
		{
			Log.Info($"uid={player.Uid} techId={techId} lv{attr.Val}->{attr.Val + 1} need cash {spendType}x{spendAmount} have {balance}, not enough");
			error = "tip.cash_not_enough";
			return false;
		}
		wallet.Val = balance - spendAmount;
		player.Attributes.SyncTo(sync, wallet);
		attr.Val++;
		newLevel = attr.Val;
		sync.Custom[player.ToPackedAttrKey(124u, techId)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(124u, techId)] = attr.Val;
		Log.Info($"uid={player.Uid} techId={techId} -> lv{newLevel}/{maxLevel} levelRow={nextLevelId} cost cash {spendType}x{spendAmount} {balance}->{wallet.Val}");
		return true;
	}
}
