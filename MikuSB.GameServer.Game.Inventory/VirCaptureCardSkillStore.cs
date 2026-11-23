using System;
using System.Collections.Generic;
using MikuSB.Database;

namespace MikuSB.GameServer.Game.Inventory;

/// <summary>
/// 伙伴技能等级的持久层（第 342 节）。
///
/// 客户端 <c>uw_vircapture_skillup.ShowMain</c> 用
/// <c>pItem:GetAffixKeyAndValue(AFFIX_SKILL1 + i - 1)</c> 读 (技能Id, 等级)，
/// 也就是说技能等级就摆在 <c>Item.Enhance.Affixs</c> 的槽 4..6 里。
/// 但 Affixs 只存在于子类对象上，落库后会被 SqlSugar 按基类还原丢掉，
/// 所以真正的等级存在本表，登录时再覆盖回 Affixs。
/// </summary>
public static class VirCaptureCardSkillStore
{
	private static readonly object Gate = new object();

	private static readonly Dictionary<int, VirCaptureCardSkillData> Cache = new Dictionary<int, VirCaptureCardSkillData>();

	private static bool _tableReady;

	/// <summary>三个技能等级打包成一个 uint，每级占 4 bit（1..6）。</summary>
	public static uint Pack(uint l1, uint l2, uint l3)
	{
		return (l1 & 0xFu) | ((l2 & 0xFu) << 4) | ((l3 & 0xFu) << 8);
	}

	public static uint Unpack(uint packed, int index)
	{
		return (packed >> (index * 4)) & 0xFu;
	}

	public static VirCaptureCardSkillData Get(int uid)
	{
		lock (Gate)
		{
			if (Cache.TryGetValue(uid, out VirCaptureCardSkillData cached) && cached != null)
			{
				return cached;
			}
		}
		if (!_tableReady)
		{
			lock (Gate)
			{
				if (!_tableReady)
				{
					try
					{
						DatabaseHelper.InitializeSqliteTable<VirCaptureCardSkillData>();
					}
					catch (Exception)
					{
					}
					_tableReady = true;
				}
			}
		}
		VirCaptureCardSkillData data = DatabaseHelper.GetInstanceOrCreateNew<VirCaptureCardSkillData>(uid);
		if (data.Levels == null)
		{
			data.Levels = new Dictionary<uint, uint>();
		}
		lock (Gate)
		{
			Cache[uid] = data;
		}
		return data;
	}

	public static uint GetPacked(int uid, uint cardUid)
	{
		VirCaptureCardSkillData data = Get(uid);
		uint value;
		return data.Levels.TryGetValue(cardUid, out value) ? value : 0u;
	}

	public static void SetPacked(int uid, uint cardUid, uint packed)
	{
		VirCaptureCardSkillData data = Get(uid);
		if (packed == 0u)
		{
			data.Levels.Remove(cardUid);
		}
		else
		{
			data.Levels[cardUid] = packed;
		}
	}

	public static void Remove(int uid, uint cardUid)
	{
		Get(uid).Levels.Remove(cardUid);
	}

	public static void Save(int uid)
	{
		DatabaseHelper.SaveDatabaseType(Get(uid));
	}

	/// <summary>把存档里的等级盖回卡的 Affixs（登录/新建卡后调用）。</summary>
	public static void Apply(int uid, MonsterCardItemInfo card)
	{
		if (card == null)
		{
			return;
		}
		uint packed = GetPacked(uid, card.UniqueId);
		if (packed == 0u)
		{
			return;
		}
		card.EnsureAffixs();
		for (int i = 0; i < 3; i++)
		{
			uint level = Unpack(packed, i);
			if (level >= 1u && level <= 6u)
			{
				card.SetSkillLevel(4 + i, level);
			}
		}
	}

	/// <summary>把卡上当前的三个技能等级写回存档。</summary>
	public static void Capture(int uid, MonsterCardItemInfo card)
	{
		if (card == null)
		{
			return;
		}
		card.EnsureAffixs();
		SetPacked(uid, card.UniqueId, Pack(card.GetSkillLevel(4), card.GetSkillLevel(5), card.GetSkillLevel(6)));
	}
}
