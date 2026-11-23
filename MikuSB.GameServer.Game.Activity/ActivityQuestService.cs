using System;
using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 活动任务（Resources/achievement/activity_quest.json → GameData.ActivityQuestData）的进度 —— 第 438 节。
///
/// 背景：第 34 期「星祈心愿」的重复任务（23001~23007：心愿传递 / 完成公务 / 购置年货 /
/// 日常训练 / 团队协作 / 收集情报 / 精进技巧）在界面上永远是 0/N，做了也没反应。
///
/// 客户端读进度的路径（反汇编 _G.Achievement 得到）：
///   Achievement.GetProgres(nGroupId, nId) → GetBits(me:GetAttribute(nGroupId, nId), 1, 31)
///   Achievement.IsReceive(cfg)            → bit0
///   activity_quest.json 里这些任务的 nGroupId = 7（和回归活动、农场、拼图任务同一组）。
///
/// 服务端此前只有 achievement.json（属性组 2）在有行为推进；活动任务这一组 7
/// 除了少数几个活动自己写，没人按通用行为推进，所以星祈心愿的进度恒为 0。
/// 这里按任务的 Type 统一累加属性 7/任务ID —— bit0 保留「已领取」标记，bit1 起是进度。
/// </summary>
public static class ActivityQuestService
{
	/// <summary>活动任务属性组号（客户端 Achievement.GetProgres 的 nGroupId）。</summary>
	public const uint QuestGid = 7u;

	private const uint ClaimedBit = 1u;

	private const int ProgressShift = 1;

	private static Dictionary<int, List<uint>>? _byType;

	private static Dictionary<int, List<uint>> ByType()
	{
		if (_byType != null)
		{
			return _byType;
		}
		Dictionary<int, List<uint>> map = new Dictionary<int, List<uint>>();
		foreach (KeyValuePair<uint, ActivityQuestExcel> pair in GameData.ActivityQuestData)
		{
			int type = (int)pair.Value.Type;
			if (!map.TryGetValue(type, out List<uint>? list))
			{
				list = new List<uint>();
				map[type] = list;
			}
			list.Add(pair.Key);
		}
		_byType = map;
		return map;
	}

	private static uint Need(ActivityQuestExcel quest)
	{
		return quest.CheckValue == 0 ? 1u : quest.CheckValue;
	}

	/// <summary>把某个 Type 的所有活动任务推进 amount 点（不超过各自 CheckValue）。</summary>
	public static void Add(PlayerInstance player, uint type, int amount, NtfSyncPlayer? sync = null)
	{
		if (player == null || amount <= 0)
		{
			return;
		}
		if (!ByType().TryGetValue((int)type, out List<uint>? ids) || ids.Count == 0)
		{
			return;
		}
		bool changed = false;
		foreach (uint id in ids)
		{
			if (!GameData.ActivityQuestData.TryGetValue(id, out ActivityQuestExcel quest))
			{
				continue;
			}
			uint need = Need(quest);
			PlayerAttr attr = player.Attributes.GetOrCreate(QuestGid, id);
			uint cur = (attr.Val >> ProgressShift) & 0x7FFFFFFFu;
			if (cur >= need)
			{
				continue;
			}
			uint next = Math.Min(cur + (uint)amount, need);
			attr.Val = (attr.Val & ClaimedBit) | (next << ProgressShift);
			changed = true;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
		if (changed)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
	}

	/// <summary>直接把某个 Type 的活动任务标记为完成（用于「做一次」类任务）。</summary>
	public static void Complete(PlayerInstance player, uint type, NtfSyncPlayer? sync = null)
	{
		Add(player, type, 1000000, sync);
	}

	/// <summary>把某个 Type 的活动任务当前进度塞进同步包（不改值）。</summary>
	public static void Sync(PlayerInstance player, uint type, NtfSyncPlayer sync)
	{
		if (player == null || sync == null)
		{
			return;
		}
		if (!ByType().TryGetValue((int)type, out List<uint>? ids))
		{
			return;
		}
		foreach (uint id in ids)
		{
			PlayerAttr? attr = player.Data.Attrs.Find((PlayerAttr x) => x.Gid == QuestGid && x.Sid == id);
			if (attr != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
	}
}
