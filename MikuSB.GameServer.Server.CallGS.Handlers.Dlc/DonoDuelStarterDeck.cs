using System;
using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 463 节：首次进入「探索多诺卡」的初始卡组 + 肉鸽编队。
///
/// 真实现象：第一次进探索多诺卡（关卡 5500/5501），局内编队界面一片空白
/// （左侧 5 个空槽 + 「暂无多诺卡」），引导 64107 第一步就要求点卡上阵，
/// 没有卡可点，教程直接往下走。
///
/// 根因链（客户端字节码确认）：
///   DonoDuelRogue.Req_EnterLevel → DonoDuelRogue.InitFightBag()
///     → 遍历 DonoDuel.GetFormation() → DonoDuel.GetRogueFormation()
///       → 读属性组 185 的 10~13（每格高低 16 位各一张卡）
///   本服这三个属性一直是 0（肉鸽编队从未写过），于是局内背包 tbFightBag 为空，
///   界面「暂无多诺卡」。而 donoduelformation 在 ADonoRogueGameMode 里
///   bOpenBag 恒为 true，列表读的是 tbFightBag（而不是 GetBagAllCard），
///   所以哪怕局外背包有卡也照样空。
///
/// 修法：
///   1) 接取剧情任务 101 时，把配置 Reward3 = {{1,1,1},{5,1,1},{9,1,1}} 三张
///      初始卡发进局外背包（原逻辑全靠任务领奖 Reward2，新档根本拿不到卡）。
///   2) 肉鸽编队（185/10~13）为空时，用背包里的卡自动填满最多 5 个位置，
///      保证 InitFightBag 有东西可读。玩家之后仍可在编队界面手动改。
///   3) 登录、接任务、进入关卡三处兜底调用，老存档重进一次即可恢复。
/// </summary>
internal static class DonoDuelStarterDeck
{
	private static readonly Logger Log = new Logger("DonoDuelStarterDeck");

	private const uint GroupId = 185u;

	private const uint CountSid = 2u;

	/// <summary>初始卡组已发标记（600+CatchID 用到 633，这里取一个不冲突的位）。</summary>
	private const uint SeededSid = 698u;

	private const uint StarterMissionId = 101u;

	private const uint RogueFormationStart = 10u;

	private const uint RogueFormationEnd = 13u;

	private const uint MaxUniqueId = 350u;

	private const int FormationSlots = 5;

	/// <summary>发初始卡（只发一次）。</summary>
	public static bool EnsureStarterCards(PlayerInstance player, NtfSyncPlayer? sync)
	{
		try
		{
			if (player.Attributes.GetValue(GroupId, SeededSid) != 0)
			{
				return false;
			}
			if (!DonoDuelTaskState.IsAccepted(player, StarterMissionId) && !DonoDuelTaskState.IsReceived(player, StarterMissionId))
			{
				return false;
			}
			Mark(player, SeededSid, sync);
			if (player.Attributes.GetValue(GroupId, CountSid) != 0)
			{
				// 补丁前的老存档已经有卡了，只补标记，不再重复发。
				return false;
			}
			if (!GameData.DonoDuelStoryTaskData.TryGetValue(StarterMissionId, out var config))
			{
				return false;
			}
			List<(uint, uint, uint)> rows = DonoDuelBag.ReadCardRows(config.Reward3);
			List<uint> granted = DonoDuelBag.GrantCards(player, rows, sync);
			if (granted.Count == 0)
			{
				return false;
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"donoduel starter deck: {granted.Count} card(s) granted for uid={player.Uid}.");
			return true;
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel starter deck failed: " + ex.Message);
			return false;
		}
	}

	/// <summary>肉鸽编队为空时自动上阵（最多 5 张，优先挑还没在任何编队里的卡）。</summary>
	public static bool EnsureRogueFormation(PlayerInstance player, NtfSyncPlayer? sync)
	{
		try
		{
			for (uint sid = RogueFormationStart; sid <= RogueFormationEnd; sid++)
			{
				if (player.Attributes.GetValue(GroupId, sid) != 0)
				{
					// 已经有编队（玩家的或之前自动填的），不动。
					return false;
				}
			}
			uint count = Math.Min(player.Attributes.GetValue(GroupId, CountSid), MaxUniqueId);
			if (count == 0)
			{
				return false;
			}
			List<uint> pick = new List<uint>();
			HashSet<uint> templates = new HashSet<uint>();
			// 第一轮：优先挑模板不重复、且还没被别的编队占用的卡（画面不会 5 张一样）。
			for (uint uniqueId = 1u; uniqueId <= count && pick.Count < FormationSlots; uniqueId++)
			{
				if (!DonoDuelBag.HasCard(player, uniqueId) || DonoDuelBag.IsInAnyFormation(player, uniqueId))
				{
					continue;
				}
				uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
				if (templateId == 0 || !templates.Add(templateId))
				{
					continue;
				}
				pick.Add(uniqueId);
			}
			// 第二轮：模板挑完了还有空位，就把剩下没上阵的卡补上。
			for (uint uniqueId = 1u; uniqueId <= count && pick.Count < FormationSlots; uniqueId++)
			{
				if (pick.Contains(uniqueId) || !DonoDuelBag.HasCard(player, uniqueId) || DonoDuelBag.IsInAnyFormation(player, uniqueId))
				{
					continue;
				}
				pick.Add(uniqueId);
			}
			if (pick.Count == 0)
			{
				// 全都被别的编队占了（竞技场/无尽），那就允许重复上阵，至少保证非空。
				for (uint uniqueId = 1u; uniqueId <= count && pick.Count < FormationSlots; uniqueId++)
				{
					if (DonoDuelBag.HasCard(player, uniqueId))
					{
						pick.Add(uniqueId);
					}
				}
			}
			if (pick.Count == 0)
			{
				return false;
			}
			for (int i = 0; i < pick.Count; i += 2)
			{
				uint sid = RogueFormationStart + (uint)(i / 2);
				if (sid > RogueFormationEnd)
				{
					break;
				}
				uint high = pick[i];
				uint low = ((i + 1 < pick.Count) ? pick[i + 1] : 0u);
				Mark(player, sid, (high << 16) | (low & 0xFFFF), sync);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			Log.Info($"donoduel rogue formation seeded with {pick.Count} card(s) for uid={player.Uid}.");
			return true;
		}
		catch (Exception ex)
		{
			Log.Warn("donoduel rogue formation seed failed: " + ex.Message);
			return false;
		}
	}

	/// <summary>登录 / 接任务 / 进关卡统一入口。</summary>
	public static void Ensure(PlayerInstance player, NtfSyncPlayer? sync)
	{
		EnsureStarterCards(player, sync);
		EnsureRogueFormation(player, sync);
	}

	private static void Mark(PlayerInstance player, uint sid, NtfSyncPlayer? sync)
	{
		Mark(player, sid, 1u, sync);
	}

	private static void Mark(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr attr = player.Attributes.Set(GroupId, sid, value);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
	}
}
