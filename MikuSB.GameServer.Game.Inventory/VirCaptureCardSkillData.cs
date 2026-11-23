using System.Collections.Generic;
using MikuSB.Database;
using SqlSugar;

namespace MikuSB.GameServer.Game.Inventory;

/// <summary>
/// 新星开拓「伙伴」的技能等级存档（第 342 节新增）。
///
/// 为什么单独开表：<c>InventoryData.Items</c> 的声明类型是 <c>BaseGameItemInfo</c>，
/// SqlSugar 反序列化时只按声明类型还原，子类 <see cref="MonsterCardItemInfo"/> 的
/// <c>Affixs</c> 会被丢掉（第 340 节的结论）。技能升阶需要**可持久化**的等级，
/// 所以把「卡 UniqueId → 三个技能等级」单独存一张表，登录时覆盖回 Affixs。
///
/// 存的是打包值：bit0..3 = 技能槽 4 的等级，bit4..7 = 槽 5，bit8..11 = 槽 6（等级 1..6）。
/// 0 表示"没有改过"，此时沿用 <see cref="MonsterCardItemInfo.RollAffixs"/> 的确定性档位。
/// </summary>
[SugarTable("vircapture_card_skill")]
public class VirCaptureCardSkillData : BaseDatabaseDataHelper
{
	[SugarColumn(IsJson = true)]
	public Dictionary<uint, uint> Levels { get; set; } = new Dictionary<uint, uint>();
}
