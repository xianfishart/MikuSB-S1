using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.WorldBoss;

public static class WorldBossService
{
	private static readonly Logger Log = new Logger("WorldBossService");

	public const uint DailyChallenges = 3u;

	private const uint MaxAttempts = 100u;

	private const uint QuestGroupId = 7u;

	private const uint PercentTaskType = 380u;

	private const uint DamageTaskType = 381u;

	private const uint ParticipateTaskType = 382u;

	public static WorldBossActExcel? SelectAct(DateTime now)
	{
		List<WorldBossActExcel> worldBossActData = GameData.WorldBossActData;
		if (worldBossActData.Count == 0)
		{
			return null;
		}
		long nowUnix = DlcRuntimeOverrides.ToUnixSeconds(now);
		foreach (WorldBossActExcel item in worldBossActData)
		{
			long num = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(item.StartTime));
			long num2 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(item.EndTime));
			if (num > 0 && num2 >= num && nowUnix >= num && nowUnix <= num2)
			{
				return item;
			}
		}
		WorldBossActExcel worldBossActExcel = (from row in worldBossActData
			where DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.StartTime)) <= nowUnix
			orderby DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(row.StartTime)) descending
			select row).FirstOrDefault();
		if (worldBossActExcel == null)
		{
			worldBossActExcel = worldBossActData[worldBossActData.Count - 1];
		}
		return worldBossActExcel;
	}

	public static WorldBossActExcel? FindAct(uint activityId, uint actId)
	{
		return GameData.WorldBossActData.FirstOrDefault((WorldBossActExcel row) => row.ActivityId == activityId && row.ActId == actId);
	}

	public static uint ResolveLevelId(uint stageId)
	{
		WorldBossLevelDesExcel value;
		return GameData.WorldBossLevelDesData.TryGetValue(stageId, out value) ? value.LevelId : 0u;
	}

	public static uint BossMaxHp(uint stageId)
	{
		uint key = ResolveLevelId(stageId);
		WorldBossLevelExcel value;
		return GameData.WorldBossLevelData.TryGetValue(key, out value) ? value.BossMaxHp : 0u;
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		WorldBossActExcel worldBossActExcel = SelectAct(DateTime.Now);
		if (worldBossActExcel != null)
		{
			Set(player, 0u, worldBossActExcel.ActivityId, sync);
			Set(player, 1u, worldBossActExcel.ActId, sync);
			uint num = uint.Parse(DateTime.Now.ToString("yyyyMMdd"));
			uint value = player.Attributes.GetValue(179u, 3u);
			if (value != num)
			{
				Set(player, 3u, num, sync);
				Set(player, 2u, 3u, sync);
			}
			else if (player.Attributes.GetValue(179u, 2u) == 0 && !HasRecordedAttempt(player, worldBossActExcel))
			{
				Set(player, 2u, 3u, sync);
			}
		}
		// 第 442 节：期数是按配置时间算出来的，可能和存档里那一期不是同一期，
		// 所以「阵容编码自愈 + 补进排行榜」对配置里的每一期都跑一遍。
		foreach (WorldBossActExcel row in GameData.WorldBossActData)
		{
			HealMembers(player, row, sync);
			PublishRank(player, row, 0u);
		}
	}

	public static uint GetChallengeCount(PlayerInstance player)
	{
		return player.Attributes.GetValue(179u, 2u);
	}

	public static int BeginAttempt(PlayerInstance player, WorldBossActExcel act, uint stageId, NtfSyncPlayer sync, IReadOnlyList<uint>? lineup)
	{
		if (!act.LevelList.Contains(stageId))
		{
			Log.Warn($"WorldBoss enter: stage {stageId} is not in act {act.ActivityId}/{act.ActId}.");
			return -1;
		}
		uint challengeCount = GetChallengeCount(player);
		if (challengeCount == 0)
		{
			return -1;
		}
		int num = FindFreeAttempt(player, act);
		if (num < 0)
		{
			return -1;
		}
		uint num2 = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)num);
		// 第 441 节：客户端 WorldBossLogic.GetAllChallengeScore / GetTopChallengeScore 遍历 100 个挑战块时，
		// 会先读 base+1 判断「这条记录属于哪一期」，不匹配就整块跳过。
		// 原先这里写 0，于是哪怕伤害记在 base+2 里，界面也永远读不出记录（已有阵容最高积分恒空）。
		Set(player, num2 + 1, act.ActivityId, sync);
		Set(player, num2 + 2, 0u, sync);
		Set(player, num2 + 6, act.ActivityId, sync);
		Set(player, num2 + 7, act.ActId, sync);
		Set(player, num2 + 8, 0u, sync);
		for (int i = 0; i < 3; i++)
		{
			uint value = ((lineup != null && i < lineup.Count) ? lineup[i] : 0u);
			Set(player, num2 + 3 + (uint)i, value, sync);
		}
		Set(player, 2u, challengeCount - 1, sync);
		if (player.Attributes.GetValue(181u, act.ReferRank2) == 0)
		{
			Set(player, 181u, act.ReferRank2, 1u, sync);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"WorldBoss enter act={act.ActivityId}/{act.ActId} stage={stageId} block={num} left={challengeCount - 1} for uid={player.Uid}.");
		return num;
	}

	public static void ApplyResult(PlayerInstance player, WorldBossActExcel act, uint stageId, uint damage, NtfSyncPlayer sync)
	{
		int num = FindPendingAttempt(player, act);
		if (num >= 0)
		{
			uint num2 = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)num);
			Set(player, num2 + 2, damage, sync);
			Set(player, num2 + 8, 1u, sync);
		}
		uint num3 = CountRecorded(player, act);
		uint num4 = BossMaxHp(stageId);
		foreach (uint allTaskId in act.AllTaskIds)
		{
			if (GameData.ActivityQuestData.TryGetValue(allTaskId, out ActivityQuestExcel value))
			{
				uint type = value.Type;
				if (1 == 0)
				{
				}
				uint num5 = type switch
				{
					380u => (num4 != 0) ? Math.Min(100u, (uint)((ulong)((long)damage * 100L) / (ulong)num4)) : 0u, 
					381u => damage, 
					382u => num3, 
					_ => 0u, 
				};
				if (1 == 0)
				{
				}
				uint num6 = num5;
				if (num6 != 0 && value.CheckValue != 0 && !AchievementClaimService.IsFinished(player, 7u, allTaskId, value.CheckValue) && num6 >= value.CheckValue)
				{
					AchievementClaimService.Complete(player, 7u, allTaskId, value.CheckValue, sync);
				}
			}
		}
		// 第 442 节：把「本服单机榜」的分数刷新一遍（取本期所有挑战块里的最高伤害）。
		PublishRank(player, act, damage);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"WorldBoss settlement act={act.ActivityId}/{act.ActId} stage={stageId} damage={damage} attempts={num3} for uid={player.Uid}.");
	}

	public static bool MarkRecorded(PlayerInstance player, WorldBossActExcel act)
	{
		int num = FindPendingAttempt(player, act);
		if (num < 0)
		{
			return true;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint num2 = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)num);
		Set(player, num2 + 8, 1u, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return true;
	}

	private static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr playerAttr = player.Attributes.Get(179u, sid);
		if (playerAttr == null)
		{
			if (value == 0)
			{
				return;
			}
			playerAttr = player.Attributes.GetOrCreate(179u, sid);
		}
		if (playerAttr.Val != value)
		{
			playerAttr.Val = value;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, playerAttr);
			}
		}
	}

	private static void Set(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr playerAttr = player.Attributes.Get(gid, sid);
		if (playerAttr == null)
		{
			if (value == 0)
			{
				return;
			}
			playerAttr = player.Attributes.GetOrCreate(gid, sid);
		}
		if (playerAttr.Val != value)
		{
			playerAttr.Val = value;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, playerAttr);
			}
		}
	}

	private static int FindFreeAttempt(PlayerInstance player, WorldBossActExcel act)
	{
		// 第 441 节：从 1 开始分配。AttemptSid(actId, idx) = 10*idx，
		// 第 0 块正好压在 179/0~179/8 这组全局字段上（活动 id / 期数 / 今日剩余次数 / 换日标记），
		// 一旦用第 0 块，进关就会把「剩余挑战次数」当成伤害写进去，
		// 客户端读出 5 点伤害这种脏记录。留空第 0 块即可。
		for (uint num = 1u; num < 100; num++)
		{
			uint num2 = AttrIds.WorldBoss.AttemptSid(act.ActId, num);
			if (player.Attributes.GetValue(179u, num2 + 6) == 0)
			{
				return (int)num;
			}
		}
		return -1;
	}

	private static int FindPendingAttempt(PlayerInstance player, WorldBossActExcel act)
	{
		for (int num = 99; num >= 0; num--)
		{
			uint num2 = AttrIds.WorldBoss.AttemptSid(act.ActId, (uint)num);
			if (player.Attributes.GetValue(179u, num2 + 6) == act.ActivityId && player.Attributes.GetValue(179u, num2 + 7) == act.ActId && player.Attributes.GetValue(179u, num2 + 8) == 0)
			{
				return num;
			}
		}
		return -1;
	}

	private static uint CountRecorded(PlayerInstance player, WorldBossActExcel act)
	{
		uint num = 0u;
		for (uint num2 = 0u; num2 < 100; num2++)
		{
			uint num3 = AttrIds.WorldBoss.AttemptSid(act.ActId, num2);
			if (player.Attributes.GetValue(179u, num3 + 6) == act.ActivityId && player.Attributes.GetValue(179u, num3 + 8) != 0)
			{
				num++;
			}
		}
		return num;
	}

	private static bool HasRecordedAttempt(PlayerInstance player, WorldBossActExcel act)
	{
		return CountRecorded(player, act) != 0;
	}

	/// <summary>
	/// 把阵容成员（角色 Guid）转换成客户端认的「成员编码」—— 第 442 节。
	///
	/// 客户端 <c>WorldBossLogic.GetMemberGDPL(v)</c> 会把 v 拆成 <c>v/100</c> 和 <c>v%100</c>，
	/// 也就是「角色 detail（3 位）+ particular（2 位）」，然后拼成 "DDDPP" 去取头像。
	/// 第 441 节直接写的是编队里的角色 Guid（40/44/16），拆出来 detail=0，记录行头像全空。
	/// </summary>
	public static uint EncodeMember(PlayerInstance player, uint characterGuid)
	{
		if (characterGuid == 0)
		{
			return 0u;
		}
		CharacterInfo? character = player.CharacterManager?.GetCharacterByGUID(characterGuid);
		if (character == null)
		{
			return 0u;
		}
		// CharacterInfo.TemplateId = (level &lt;&lt; 48) | (particular &lt;&lt; 32) | (detail &lt;&lt; 16) | genre
		uint detail = (uint)((character.TemplateId >> 16) & 0xFFFF);
		uint particular = (uint)((character.TemplateId >> 32) & 0xFFFF);
		if (detail == 0 || detail > 999 || particular > 99)
		{
			return 0u;
		}
		return detail * 100 + particular;
	}

	/// <summary>
	/// 阵容标识串，格式与客户端 <c>GetLineupIdentifier</c> 完全一致：
	/// 每个成员 format("%03d%02d", detail, particular)，按字符串排序后无缝拼接。
	/// 排行榜里「这条记录是不是我的」就是拿它跟本地阵容的标识串比。
	/// </summary>
	public static string LineupIdentifier(IEnumerable<uint> encodedMembers)
	{
		List<string> parts = new List<string>();
		foreach (uint value in encodedMembers)
		{
			if (value == 0)
			{
				continue;
			}
			parts.Add((value / 100).ToString("D3") + (value % 100).ToString("D2"));
		}
		parts.Sort(StringComparer.Ordinal);
		return string.Concat(parts);
	}

	/// <summary>旧存档自愈：把挑战块里残留的「角色 Guid」改写成「detail*100+particular」。</summary>
	public static void HealMembers(PlayerInstance player, WorldBossActExcel act, NtfSyncPlayer? sync)
	{
		for (uint i = 0u; i < 100; i++)
		{
			uint num = AttrIds.WorldBoss.AttemptSid(act.ActId, i);
			if (player.Attributes.GetValue(179u, num + 6) != act.ActivityId)
			{
				continue;
			}
			for (uint k = 3u; k <= 5; k++)
			{
				uint value = player.Attributes.GetValue(179u, num + k);
				if (value == 0 || value >= 1000)
				{
					continue;
				}
				uint encoded = EncodeMember(player, value);
				if (encoded != 0 && encoded != value)
				{
					Set(player, num + k, encoded, sync);
					Log.Info($"WorldBoss 阵容编码修正 act={act.ActivityId}/{act.ActId} sid={num + k} {value} -> {encoded} uid={player.Uid}");
				}
			}
		}
	}

	/// <summary>把本期最高伤害写进本服单机榜（WorldBoss_Rank_&lt;ReferRank1&gt;）。</summary>
	public static void PublishRank(PlayerInstance player, WorldBossActExcel act, uint fallbackDamage)
	{
		if (act.ReferRank1 == 0)
		{
			return;
		}
		uint best = 0u;
		uint[] bestMembers = new uint[3];
		for (uint i = 0u; i < 100; i++)
		{
			uint num = AttrIds.WorldBoss.AttemptSid(act.ActId, i);
			if (player.Attributes.GetValue(179u, num + 6) != act.ActivityId || player.Attributes.GetValue(179u, num + 8) == 0)
			{
				continue;
			}
			uint value = player.Attributes.GetValue(179u, num + 2);
			if (value > best)
			{
				best = value;
				bestMembers[0] = player.Attributes.GetValue(179u, num + 3);
				bestMembers[1] = player.Attributes.GetValue(179u, num + 4);
				bestMembers[2] = player.Attributes.GetValue(179u, num + 5);
			}
		}
		if (best == 0)
		{
			best = fallbackDamage;
		}
		try
		{
			WorldBossRankService.Record(WorldBossRankService.RankNameOf(act.ReferRank1), (ulong)player.Uid, player.Data?.Name, LineupIdentifier(bestMembers), best, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		}
		catch (Exception ex)
		{
			Log.Warn($"WorldBoss 榜单写入失败：{ex.GetType().Name}: {ex.Message}");
		}
	}
}
