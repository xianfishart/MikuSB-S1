using System;
using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Inventory;

/// <summary>
/// 怪物卡（新星开拓「伙伴」）物品：把属性档位放进 <c>Item.Enhance.Affixs</c> 下发。
///
/// 客户端 <c>UMonsterCard:AffixAttrLevel(EAttributeType)</c> 读的就是这串值，
/// 而且它的参数是 EAttributeType 枚举（9=Attack / 12=Defence / 0=Health），
/// 所以槽里的 key 必须写成这些枚举值，不能写 1/2/3 下标——
/// 写 1/2/3 时客户端拿 9/12/0 去比对永远匹配不上，返回 0，
/// 而 <c>tbAttrFlag[0]</c> 是 nil，右侧「基础属性」就整片空白（第 340/341 节）。
/// 技能槽 4..6、芯片槽 7..9 的 key 是技能/芯片 Id，暂留 0。
/// 之前怪物卡是裸 BaseGameItemInfo，ToProto 只发 Id/Template/Count/Flag，
/// 客户端拿到的档位全是 0，收编分配右侧「基础属性」就整片空白（2026-11-15 第 340 节）。
///
/// 档位不落库：<c>InventoryData.Items</c> 的声明类型是 <c>BaseGameItemInfo</c>，
/// SqlSugar 反序列化会丢子类字段。这里改成由 UniqueId + TemplateId 确定性推导，
/// 登录时 <see cref="InventoryManager.EnsureMonsterCardInfos"/> 重建一遍即可保证前后一致。
/// </summary>
public class MonsterCardItemInfo : BaseGameItemInfo
{
	/// <summary>Enhance.Affixs 的槽位数量（攻击/防御/生命 + 技能×3 + 芯片×3）。</summary>
	public const int AffixSlotCount = 9;

	/// <summary>1 = 攻击 / 2 = 防御 / 3 = 生命。</summary>
	public const int AttributeSlotCount = 3;

	public List<uint> Affixs { get; set; } = new List<uint>();

	public override Item ToProto()
	{
		EnsureAffixs();
		Item item = new Item
		{
			Id = UniqueId,
			Template = TemplateId,
			Count = ItemCount,
			Flag = (uint)Flag,
			Enhance = new Enhance
			{
				Level = Level,
				Exp = Exp
			}
		};
		item.Enhance.Affixs.AddRange(Affixs);
		return item;
	}

	/// <summary>
	/// 三个属性槽的 key，用的是客户端 <c>EAttributeType</c> 枚举值：
	/// 9 = Attack、12 = Defence、0 = Health（已用 GetEnumValueAsString 实测确认）。
	///
	/// 注意不是 1/2/3：VirCapture.AFFIX_ATTACK=1 那套常量是**槽位下标**，
	/// 客户端 AffixAttrLevel(type) 是拿 type 去和槽里的 key 比对，
	/// 传进去的正是 9/12/0（见 umg_vircapture_formation.ShowAttributePanel 反汇编）。
	/// </summary>
	private static readonly uint[] AttributeTypes = new uint[] { 9u, 12u, 0u };

	/// <summary>
	/// 技能槽 4..6 的候选技能 Id（对应 dlc/vircapture/skilllist.json 的 Id，1..25）。
	/// 服务端没有加载这张表，这里按配置范围硬编码；如果以后新增技能，改这里即可。
	/// </summary>
	private static readonly uint[] SkillPool = BuildRange(1u, 25u);

	/// <summary>芯片槽 7..9 的候选芯片 Id（chiplist.json 的 Id，1..11）。</summary>
	private static readonly uint[] ChipPool = BuildRange(1u, 11u);

	private static uint[] BuildRange(uint from, uint to)
	{
		uint[] arr = new uint[to - from + 1];
		for (uint i = from; i <= to; i++)
		{
			arr[i - from] = i;
		}
		return arr;
	}

	/// <summary>槽位（1 基）的等级：Affixs[2*(slot-1)+1]。</summary>
	public uint GetSlotLevel(int slot)
	{
		if (Affixs == null)
		{
			return 0u;
		}
		int idx = (slot - 1) * 2 + 1;
		return (idx >= 0 && idx < Affixs.Count) ? Affixs[idx] : 0u;
	}

	/// <summary>槽位（1 基）的 Id：Affixs[2*(slot-1)]。</summary>
	public uint GetSlotId(int slot)
	{
		if (Affixs == null)
		{
			return 0u;
		}
		int idx = (slot - 1) * 2;
		return (idx >= 0 && idx < Affixs.Count) ? Affixs[idx] : 0u;
	}

	public void SetSlotLevel(int slot, uint level)
	{
		EnsureAffixs();
		int idx = (slot - 1) * 2 + 1;
		if (idx >= 0 && idx < Affixs.Count)
		{
			Affixs[idx] = level;
		}
	}

	public uint GetSkillLevel(int slot)
	{
		return GetSlotLevel(slot);
	}

	public void SetSkillLevel(int slot, uint level)
	{
		SetSlotLevel(slot, level);
	}

	/// <summary>技能槽 4..6 里找指定技能 Id 的槽位；找不到返回 0。</summary>
	public int FindSkillSlot(uint skillId)
	{
		EnsureAffixs();
		for (int slot = 4; slot <= 6; slot++)
		{
			if (GetSlotId(slot) == skillId)
			{
				return slot;
			}
		}
		return 0;
	}

	public void EnsureAffixs()
	{
		// 首项必须是攻击的属性类型（9），且技能槽 4 的 Id（下标 6）已有数据，
		// 否则说明是旧格式（属性 1/2/3 下标、或技能芯片全 0），重算一遍。
		// 注意下标 4 是"生命"的属性 key（EAttributeType.Health = 0），永远为 0，不能用来判断。
		if (Affixs != null && Affixs.Count >= AffixSlotCount
			&& Affixs[0] == AttributeTypes[0] && Affixs[6] != 0u)
		{
			return;
		}
		Affixs = RollAffixs(UniqueId, TemplateId);
	}

	/// <summary>
	/// 确定性档位：同一张卡（UniqueId + TemplateId）永远得到同一组值，不需要持久化。
	/// 品质（Color）越高越容易出高档，和 attributepool.json 的 0.9/1.0/1.1 倍率池同构。
	///
	/// 编码是 **成对** 的 [key, 档位]：客户端 <c>GetAffix(i)</c> 返回的 TArray 就是
	/// { key, level } 两个 uint（实测 GetAffix(1..9) 都是长度 2）。
	/// 属性槽 1..3 的 key 用 EAttributeType（9/12/0），技能槽 4..6、芯片槽 7..9 暂留 0。
	/// </summary>
	public static List<uint> RollAffixs(uint uniqueId, ulong templateId)
	{
		uint color = 0u;
		if (GameData.MonsterCardData.TryGetValue(templateId, out MonsterCardExcel cfg))
		{
			color = cfg.Color;
		}
		uint state = uniqueId * 2654435761u ^ (uint)(templateId & 0xFFFFFFFFu) ^ (uint)(templateId >> 32);
		List<uint> result = new List<uint>(AffixSlotCount);
		for (int i = 0; i < AttributeSlotCount; i++)
		{
			uint affixId = AttributeTypes[i];
			state = state * 1664525u + 1013904223u;
			uint roll = (state >> 16) % 100u;
			uint level;
			if (color >= 6)
			{
				level = ((roll < 10) ? 2u : ((roll < 40) ? 3u : 4u));
			}
			else if (color == 5)
			{
				level = ((roll < 25) ? 1u : ((roll < 75) ? 2u : 3u));
			}
			else
			{
				level = ((roll < 55) ? 1u : ((roll < 95) ? 2u : 3u));
			}
			result.Add(affixId);
			result.Add(level);
		}

		// 技能槽 4..6：(技能 Id, 等级 1..MaxLevel=6)，三个不重复
		AppendPicks(result, SkillPool, 6u, ref state);

		// 芯片槽 7..9：(芯片 Id, 等级 1..3)
		AppendPicks(result, ChipPool, 3u, ref state);

		while (result.Count < AffixSlotCount)
		{
			result.Add(0u);
		}
		return result;
	}

	/// <summary>
	/// 从池里挑 3 个不重复的 Id，各配一个 1..maxLevel 的等级，按 [id, lv] 追加。
	/// 客户端 ShowSkillPanel 用 GetAffixKeyAndValue(AFFIX_SKILL1+i-1) 取 (id, level)，
	/// ShowChipPanel 用 GetAffixKeyAndValue(AFFIX_CHIP1+i-1) 再走 GetChipConfig(id, level)。
	/// </summary>
	private static void AppendPicks(List<uint> result, uint[] pool, uint maxLevel, ref uint state)
	{
		List<uint> rest = new List<uint>(pool);
		for (int i = 0; i < 3 && rest.Count > 0; i++)
		{
			state = state * 1664525u + 1013904223u;
			int idx = (int)((state >> 16) % (uint)rest.Count);
			uint id = rest[idx];
			rest.RemoveAt(idx);
			state = state * 1664525u + 1013904223u;
			uint level = 1u + (state >> 16) % maxLevel;
			result.Add(id);
			result.Add(level);
		}
	}
}
