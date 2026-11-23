using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「开拓目标」进度引擎（第 343 节新增）。
///
/// 背景：客户端根本不自己算任务进度，而是读玩家属性 <c>(133, MissionID)</c>：
/// <code>
///   Achievement.GetProgres(gid,id) = GetBits(me:GetAttribute(gid,id), 1, 31)
///   Achievement.IsReceive(cfg)     = GetBits(me:GetAttribute(gid,id), 0, 1)
///   Achievement.IsFinished(cfg)    = (cfg.nCheckValue &lt;= prog)
/// </code>
/// （gid 是加载配置时写死的 133，实测 GetMissionGroup(1) 的条目 nGroupId 全是 133。）
/// 之前只有 <c>VirCaptureTask_DoSomething</c> 会 +1，而客户端从来不调它，
/// 于是 133 组属性一条都没有，所有任务永远停在「未完成」，点领取只会提示 achievement.NotFinished。
///
/// 现在改成服务端自己记事件计数（<see cref="VirCaptureMissionCounterData"/>），
/// 再按任务 Type 换算成"累计值"写回属性位；领取时 VirCaptureTaskStore.MarkClaimed 只置 bit0，
/// 不会再被 SyncAll 覆盖（SyncAll 保留 bit0）。
///
/// 任务 Type 与判据对照（来自 vircapture_mission.json + 文案）：
///   2   闯关吧指定关卡通关         → ClearedLevels 里是否有这条等级
///   11  累计登录天数              → login_day
///   12  累计蓝图币                → blueprint_coin
///   201 累计劝降伙伴              → gacha
///   203 试炼积分                  → trial_score
///   204 开拓等级                  → 128/3
///   212 累计钓鱼                  → fish
///   213 累计技能升阶              → skill_up
///   214 累计解雇伙伴              → dismiss
///   215 累计快捷劝降              → quick_gacha
///   216 累计击败敌人              → kill
///   219 蓝图岛采集/制作等          → build_collect
///   261 制作鱼饵                  → fish_bait
///   262 钓鱼总评分                → fish_score
///   263 啸天锤头鲨评分            → fish_shark
///   309 蓝图岛等级                → build_level
///   310 聘用员工数                → build_staff
///   312 建造建筑数                → build_done
///   314 蓝图岛钓鱼                → fish_build
/// 没有数据源的类型（钓鱼、蓝图岛、试炼）暂时恒为 0，需要那些系统接上事件后再 Add 即可。
/// </summary>
public static class VirCaptureMissionProgress
{
	public const uint Gid = 133u;

	/// <summary>bit0 = 已领取，bit1..31 = 进度。</summary>
	public const uint ClaimedBit = 1u;

	public const uint ProgressMask = 0x7FFFFFFFu;

	// ---- 计数器键 ----
	public const string KeyLoginDay = "login_day";

	public const string KeyGacha = "gacha";

	public const string KeyQuickGacha = "quick_gacha";

	public const string KeySkillUp = "skill_up";

	public const string KeyDismiss = "dismiss";

	public const string KeyKill = "kill";

	public const string KeyFish = "fish";

	public const string KeyFishScore = "fish_score";

	public const string KeyFishShark = "fish_shark";

	public const string KeyFishBait = "fish_bait";

	public const string KeyFishBuild = "fish_build";

	public const string KeyTrialScore = "trial_score";

	public const string KeyBlueprintCoin = "blueprint_coin";

	/// <summary>上次同步时的蓝图币余额基线（第 513 节，用于差值追踪「累计获得」）。</summary>
	public const string KeyBlueprintCoinSeen = "blueprint_coin_seen";

	public const string KeyBuildCollect = "build_collect";

	public const string KeyBuildLevel = "build_level";

	public const string KeyBuildStaff = "build_staff";

	public const string KeyBuildDone = "build_done";

	/// <summary>记一次事件（可正可负），返回累加后的值。</summary>
	public static long Add(PlayerInstance player, string key, long amount = 1L)
	{
		if (player == null || string.IsNullOrEmpty(key) || amount == 0L)
		{
			return 0L;
		}
		long value = VirCaptureMissionCounterStore.Add((int)player.Uid, key, amount);
		VirCaptureMissionCounterStore.Save((int)player.Uid);
		return value;
	}

	/// <summary>只涨不落（评分/存量这类）。</summary>
	public static long RaiseTo(PlayerInstance player, string key, long value)
	{
		if (player == null || string.IsNullOrEmpty(key))
		{
			return 0L;
		}
		long result = VirCaptureMissionCounterStore.RaiseTo((int)player.Uid, key, value);
		VirCaptureMissionCounterStore.Save((int)player.Uid);
		return result;
	}

	/// <summary>写一条任务的进度（保留 bit0 的已领取标记）。返回是否发生变化。</summary>
	public static bool WriteProgress(PlayerInstance player, uint missionId, uint progress, NtfSyncPlayer? sync)
	{
		if (player == null || missionId == 0)
		{
			return false;
		}
		if (progress > ProgressMask)
		{
			progress = ProgressMask;
		}
		PlayerAttr attr = player.Attributes.GetOrCreate(Gid, missionId);
		uint next = (attr.Val & ClaimedBit) | (progress << 1);
		if (next == attr.Val)
		{
			return false;
		}
		attr.Val = next;
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return true;
	}

	/// <summary>按事件计数 + 现有属性算出某条任务当前的"累计值"。</summary>
	public static uint Evaluate(PlayerInstance player, VirCaptureMissionExcel mission, VirCaptureMissionCounterData counters)
	{
		if (player == null || mission == null)
		{
			return 0u;
		}
		long value;
		switch (mission.Type)
		{
		case 2u:
			// 闯关吧：配置里的 Param 服务端没解析，退化成"通关关卡总数"，够用即可。
			return (uint)Math.Min(counters.ClearedLevels.Count, int.MaxValue);
		case 11u:
			return Take(counters, KeyLoginDay);
		case 12u:
			return Take(counters, KeyBlueprintCoin);
		case 201u:
			return Take(counters, KeyGacha);
		case 203u:
			return Take(counters, KeyTrialScore);
		case 204u:
			return player.Attributes.GetValue(128u, 3u);
		case 212u:
			return Take(counters, KeyFish);
		case 213u:
			return Take(counters, KeySkillUp);
		case 214u:
			return Take(counters, KeyDismiss);
		case 215u:
			return Take(counters, KeyQuickGacha);
		case 216u:
			return Take(counters, KeyKill);
		case 219u:
			return Take(counters, KeyBuildCollect);
		case 261u:
			return Take(counters, KeyFishBait);
		case 262u:
			return Take(counters, KeyFishScore);
		case 263u:
			return Take(counters, KeyFishShark);
		case 309u:
			// 第 513 节：岛屿等级不用计数器（KeyBuildLevel 从来没人写，301~304 永远 0），
			// 直接读 128 组的岛屿等级（新星开拓只有 326 一座岛）。
			return VirCaptureLevelState.GetMapLevel(player, 326u);
		case 310u:
			// 第 516 节：「累计聘用员工」类（Excel 没解析 Param，无法区分带参的一般/厉害/强力档，
			// 那些判据也未确认），进度一律按已拥有员工数走；带参档会少算/不准，后面确认判据再单修。
			return (uint)VirCaptureStaffState.OwnedCount(player);
		case 312u:
			return Take(counters, KeyBuildDone);
		case 314u:
			return Take(counters, KeyFishBuild);
		default:
			value = 0L;
			break;
		}
		return 0u;
	}

	private static uint Take(VirCaptureMissionCounterData counters, string key)
	{
		if (counters.Counters.TryGetValue(key, out long value) && value > 0L)
		{
			return (uint)Math.Min(value, int.MaxValue);
		}
		return 0u;
	}

	/// <summary>
	/// 第 513 节：「累计获得蓝图币」（Type 12，经营目标 305~312）靠余额差值追踪。
	/// 蓝图币余额在属性 (1,111)（cashId 55 = VirCaptureStaffTable.CashId(326)）。
	/// 余额增加的差额记进 <see cref="KeyBlueprintCoin"/>；消费导致的减少不计。
	/// 首次接入的老存档没有基线，把当前余额当作累计下限——累计获得 ≥ 当前余额恒成立，
	/// 宁可少记不多记，之后每笔入账都会补上。
	/// </summary>
	public static void SyncBlueprintCoin(PlayerInstance player)
	{
		if (player == null)
		{
			return;
		}
		int uid = (int)player.Uid;
		VirCaptureMissionCounterData data = VirCaptureMissionCounterStore.Get(uid);
		long balance = player.Attributes.GetValue(1u, 111u);
		long seen = data.Counters.TryGetValue(KeyBlueprintCoinSeen, out long value) ? value : -1L;
		if (seen < 0L)
		{
			data.Counters[KeyBlueprintCoinSeen] = balance;
			if (balance > 0L)
			{
				VirCaptureMissionCounterStore.Add(uid, KeyBlueprintCoin, balance);
			}
			VirCaptureMissionCounterStore.Save(uid);
			return;
		}
		data.Counters[KeyBlueprintCoinSeen] = balance;
		if (balance > seen)
		{
			VirCaptureMissionCounterStore.Add(uid, KeyBlueprintCoin, balance - seen);
		}
		VirCaptureMissionCounterStore.Save(uid);
	}

	/// <summary>
	/// 全量刷新所有任务进度。登录（sync = null）与每次事件后各跑一次。
	/// 返回真正发生变化的条数，便于日志确认。
	/// </summary>
	public static int SyncAll(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player == null || GameData.VirCaptureMissionData.Count == 0)
		{
			return 0;
		}
		SyncBlueprintCoin(player);
		VirCaptureMissionCounterData counters = VirCaptureMissionCounterStore.Get((int)player.Uid);
		int changed = 0;
		foreach (VirCaptureMissionExcel mission in GameData.VirCaptureMissionData.Values)
		{
			uint progress = Evaluate(player, mission, counters);
			if (WriteProgress(player, mission.MissionId, progress, sync))
			{
				changed++;
			}
		}
		if (changed > 0 && sync == null)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return changed;
	}

	/// <summary>把所有任务的进度直接顶到 CheckValue（GM 用：一键可领取）。</summary>
	public static int CompleteAll(PlayerInstance player, NtfSyncPlayer sync)
	{
		if (player == null)
		{
			return 0;
		}
		int changed = 0;
		foreach (VirCaptureMissionExcel mission in GameData.VirCaptureMissionData.Values)
		{
			if (WriteProgress(player, mission.MissionId, mission.CheckValue, sync))
			{
				changed++;
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return changed;
	}

	/// <summary>把某一条任务顶到 CheckValue。</summary>
	public static bool CompleteOne(PlayerInstance player, uint missionId, uint? value, NtfSyncPlayer sync)
	{
		if (!GameData.VirCaptureMissionData.TryGetValue(missionId, out VirCaptureMissionExcel mission))
		{
			return false;
		}
		uint target = value ?? mission.CheckValue;
		if (!WriteProgress(player, missionId, target, sync))
		{
			return false;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return true;
	}

	/// <summary>登录：记累计登录天数（按自然日去重）。返回是否记过。</summary>
	public static bool MarkLoginDay(PlayerInstance player)
	{
		if (player == null)
		{
			return false;
		}
		VirCaptureMissionCounterData data = VirCaptureMissionCounterStore.Get((int)player.Uid);
		string today = DateTime.Now.ToString("yyyyMMdd");
		if (data.LastLoginDay == today)
		{
			return false;
		}
		data.LastLoginDay = today;
		VirCaptureMissionCounterStore.Add((int)player.Uid, KeyLoginDay, 1L);
		VirCaptureMissionCounterStore.Save((int)player.Uid);
		return true;
	}

	/// <summary>把当前背包里的伙伴数当"至少劝降过这么多只"，用于老号首次接入时对齐。</summary>
	public static void SeedFromInventory(PlayerInstance player)
	{
		if (player == null)
		{
			return;
		}
		int uid = (int)player.Uid;
		if (VirCaptureMissionCounterStore.Peek(uid, KeyGacha) > 0L)
		{
			return;
		}
		int cards = player.InventoryManager.InventoryData.Items.Values.Count(x => x.ItemType == MikuSB.Enums.Item.ItemTypeEnum.TYPE_MONSTER_CARD);
		if (cards > 0)
		{
			VirCaptureMissionCounterStore.Add(uid, KeyGacha, cards);
			VirCaptureMissionCounterStore.Save(uid);
		}
	}
}
