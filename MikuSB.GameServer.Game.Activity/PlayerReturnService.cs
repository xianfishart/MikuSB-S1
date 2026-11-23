using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Game.Activity;

public static class PlayerReturnService
{
	private static class Attrs
	{
		public const uint ActIdSid = 15u;

		public const uint LastActiveTimeSid = 16u;
	}

	private const string ReturnClass = "player_return";

	private const uint ProgressShift = 1u;

	private const uint ProgressMask = 2147483647u;

	private const uint SignedDayMask = 255u;

	private const int ClaimedDayShift = 8;

	private const uint ClaimedDayMask = 255u;

	private const long SecondsPerDay = 86400L;

	private const uint MinWindowDays = 7u;

	private static readonly Logger Log = new Logger("PlayerReturnService");

	private static uint StartTimeSid(uint actId)
	{
		return actId * 10 + 3;
	}

	private static uint SignSid(uint actId)
	{
		return actId * 10 + 4;
	}

	private static uint FirstGetSid(uint actId)
	{
		return actId * 10 + 6;
	}

	private static uint ProtectAddSid(uint actId)
	{
		return actId * 10 + 7;
	}

	private static long Now()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	}

	public static uint GetActId(PlayerInstance player)
	{
		return player.Attributes.GetValue(0u, 15u);
	}

	public static uint SelectActId()
	{
		long num = DlcRuntimeOverrides.ActivationFloor();
		uint result = 0u;
		long num2 = long.MaxValue;
		foreach (ActivityExcel value in GameData.ActivityData.Values)
		{
			if (string.Equals(value.Class, "player_return", StringComparison.OrdinalIgnoreCase) && !DlcRuntimeOverrides.IsHistoricalWindow(value.EndTime))
			{
				long num3 = DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(value.StartTime));
				if ((num == 0L || num3 >= num) && num3 != 0L && num3 < num2)
				{
					num2 = num3;
					result = value.Id;
				}
			}
		}
		return result;
	}

	public static void RollDailyState(PlayerInstance player)
	{
		Ensure(player, null);
	}

	public static void Ensure(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint num = SelectActId();
		if (num == 0)
		{
			Log.Warn($"No player_return activity inside the activated era for uid={player.Uid}.");
			return;
		}
		if (!GameData.PlayerReturnData.TryGetValue(num, out PlayerReturnExcel value))
		{
			Log.Warn($"Return activity {num} has no return.json row for uid={player.Uid}.");
			return;
		}
		uint actId = GetActId(player);
		long num2 = Now();
		uint stageCount = (GameData.PlayerReturnRewardData.TryGetValue(num, out PlayerReturnRewardExcel value2) ? ((uint)value2.StageCount) : 0u);
		uint num3 = BackDateDays(value.ActiveDay, stageCount);
		long num4 = num2 - (long)num3 * 86400L;
		uint value3 = player.Attributes.GetValue(50u, StartTimeSid(num));
		if (actId != num || value3 == 0 || value3 > num4)
		{
			bool flag = actId != num;
			SetAttr(player, 0u, 15u, num, sync);
			SetAttr(player, 50u, StartTimeSid(num), (uint)num4, sync);
			if (flag)
			{
				SetAttr(player, 50u, SignSid(num), 0u, sync);
				SetAttr(player, 50u, FirstGetSid(num), 0u, sync);
				SetAttr(player, 50u, ProtectAddSid(num), value.OfflineProtectionDay, sync);
				SetAttr(player, 0u, 16u, (uint)num2, sync);
				Log.Info($"Return activity {num} triggered for uid={player.Uid} (activeDay={value.ActiveDay}, protectAdd={value.OfflineProtectionDay}, backDate={num3}d).");
			}
			else
			{
				Log.Info($"Return activity {num}: start re-seeded to {num4} for uid={player.Uid} (was {value3}, backDate={num3}d).");
			}
		}
		AdvanceSignedDays(player, num, sync);
		MarkOpenStagesFinished(player, num, sync);
	}

	private static uint BackDateDays(uint activeDay, uint stageCount)
	{
		// 2026-11-11：按用户要求改回「配置周期」口径——回归窗口直接取
		// return.json 的 ActiveDay（14 天），起点 = 登录当天，不再把起点
		// 回拨到活动中期。旧实现回拨 min(StageCount-1, ActiveDay-7) = 7 天，
		// 导致界面只剩 7 天倒计时。阶段开放仍由 CurrentStage 按自然天数推进
		// （第 n 天开第 n 阶段，封顶 StageCount）。
		_ = activeDay;
		_ = stageCount;
		return 0u;
	}

	public static (long Start, long End) GetTimeConf(PlayerInstance player, uint actId)
	{
		uint value = player.Attributes.GetValue(50u, StartTimeSid(actId));
		uint value2 = player.Attributes.GetValue(50u, ProtectAddSid(actId));
		uint num = (GameData.PlayerReturnData.TryGetValue(actId, out PlayerReturnExcel value3) ? value3.ActiveDay : 0u);
		return (Start: value, End: value + (long)(num + value2) * 86400L);
	}

	public static async Task<JsonArray> ClaimFirstAsync(PlayerInstance player, uint actId, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		if (!GameData.PlayerReturnRewardData.TryGetValue(actId, out PlayerReturnRewardExcel rewardConf))
		{
			Log.Warn($"Return first reward: act {actId} has no return_reward row for uid={player.Uid}.");
			return granted;
		}
		if (player.Attributes.GetValue(50u, FirstGetSid(actId)) == 1)
		{
			Log.Warn($"Return first reward: act {actId} already claimed for uid={player.Uid}.");
			return granted;
		}
		List<uint[]> rows = DlcRuntimeOverrides.ReadRewardRows(rewardConf.FirstReward);
		JsonArray rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		foreach (JsonNode item in rewards)
		{
			granted.Add(item?.DeepClone());
		}
		SetAttr(player, 50u, FirstGetSid(actId), 1u, sync);
		Save(player);
		Log.Info($"Return first reward: act {actId}, {rewards.Count} row(s) granted for uid={player.Uid}.");
		return granted;
	}

	public static async Task<(JsonArray Granted, List<uint> Days)> ClaimSignAsync(PlayerInstance player, uint actId, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		List<uint> days = new List<uint>();
		var (signed, claimed) = GetSignDays(player, actId);
		if (claimed >= signed)
		{
			Log.Info($"Return sign: act {actId} nothing due ({signed} signed / {claimed} claimed) for uid={player.Uid}.");
			return (Granted: granted, Days: days);
		}
		uint dayId = claimed + 1;
		if (!GameData.PlayerReturnSignData.TryGetValue((actId, dayId), out PlayerReturnSignExcel row))
		{
			Log.Warn($"Return sign: act {actId} has no return_sign row for day {dayId} (uid={player.Uid}).");
			return (Granted: granted, Days: days);
		}
		List<uint[]> rows = DlcRuntimeOverrides.ReadRewardRows(row.SignReward);
		JsonArray rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		foreach (JsonNode item in rewards)
		{
			granted.Add(item?.DeepClone());
		}
		SetAttr(player, 50u, SignSid(actId), PackSignDays(signed, dayId), sync);
		Save(player);
		days.Add(dayId);
		Log.Info($"Return sign: act {actId} day {dayId} claimed, {rewards.Count} row(s) granted for uid={player.Uid}.");
		return (Granted: granted, Days: days);
	}

	public static async Task<JsonArray> ClaimMissionsAsync(PlayerInstance player, uint actId, bool quick, uint missionId, NtfSyncPlayer sync)
	{
		JsonArray granted = new JsonArray();
		if (!GameData.PlayerReturnRewardData.TryGetValue(actId, out PlayerReturnRewardExcel rewardConf))
		{
			Log.Warn($"Return mission reward: act {actId} has no return_reward row for uid={player.Uid}.");
			return granted;
		}
		uint openStage = Math.Min(CurrentStage(player, actId), (uint)rewardConf.StageCount);
		List<uint> candidates = new List<uint>();
		if (quick)
		{
			for (uint rank = 1u; rank <= openStage; rank++)
			{
				candidates.AddRange(rewardConf.StageQuestIds((int)rank));
			}
		}
		else
		{
			uint rank2 = FindStage(rewardConf, missionId);
			if (rank2 == 0)
			{
				Log.Warn($"Return mission reward: quest {missionId} is not a row of act {actId} (uid={player.Uid}).");
				return granted;
			}
			if (rank2 > openStage)
			{
				Log.Warn($"Return mission reward: quest {missionId} sits in stage {rank2} but only {openStage} stage(s) are open for uid={player.Uid}.");
				return granted;
			}
			candidates.Add(missionId);
		}
		bool claimedAny = false;
		foreach (uint questId in candidates.Distinct())
		{
			if (!GameData.ActivityQuestData.TryGetValue(questId, out ActivityQuestExcel quest))
			{
				Log.Warn($"Return mission reward: unknown activity quest {questId} for uid={player.Uid}.");
				continue;
			}
			PlayerAttr attr = player.Attributes.GetOrCreate(7u, questId);
			if (IsReceived(attr.Val))
			{
				continue;
			}
			uint progress = GetProgress(attr.Val);
			if (progress < quest.CheckValue)
			{
				Log.Warn($"Return mission reward: quest {questId} not finished ({progress}/{quest.CheckValue}) for uid={player.Uid}.");
				continue;
			}
			List<uint[]> rows = DlcRuntimeOverrides.ReadRewardRows(quest.Rewards);
			JsonArray rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
			foreach (JsonNode item in rewards)
			{
				granted.Add(item?.DeepClone());
			}
			attr.Val = Pack(progress, received: true);
			player.Attributes.SyncTo(sync, attr);
			claimedAny = true;
			Log.Info($"Return mission reward: quest {questId} claimed, {rewards.Count} row(s) granted for uid={player.Uid}.");
			quest = null;
		}
		if (claimedAny)
		{
			Save(player);
		}
		return granted;
	}

	public static (uint Signed, uint Claimed) GetSignDays(PlayerInstance player, uint actId)
	{
		uint value = player.Attributes.GetValue(50u, SignSid(actId));
		return (Signed: value & 0xFF, Claimed: (value >> 8) & 0xFF);
	}

	public static uint CurrentStage(PlayerInstance player, uint actId)
	{
		uint value = player.Attributes.GetValue(50u, StartTimeSid(actId));
		if (value == 0)
		{
			return 1u;
		}
		long val = (Now() - value) / 86400;
		return (uint)((int)Math.Max(0L, val) + 1);
	}

	public static uint MaxSignDay(uint actId)
	{
		uint num = 0u;
		foreach (var key in GameData.PlayerReturnSignData.Keys)
		{
			if (key.ActId == actId && key.DayId > num)
			{
				num = key.DayId;
			}
		}
		return num;
	}

	private static void AdvanceSignedDays(PlayerInstance player, uint actId, NtfSyncPlayer? sync)
	{
		uint num = MaxSignDay(actId);
		if (num != 0)
		{
			(uint Signed, uint Claimed) signDays = GetSignDays(player, actId);
			uint item = signDays.Signed;
			uint item2 = signDays.Claimed;
			uint num2 = Math.Min(CurrentStage(player, actId), num);
			if (num2 > item)
			{
				SetAttr(player, 50u, SignSid(actId), PackSignDays(num2, item2), sync);
			}
		}
	}

	private static void MarkOpenStagesFinished(PlayerInstance player, uint actId, NtfSyncPlayer? sync)
	{
		if (!GameData.PlayerReturnRewardData.TryGetValue(actId, out PlayerReturnRewardExcel value))
		{
			return;
		}
		uint num = Math.Min(CurrentStage(player, actId), (uint)value.StageCount);
		bool flag = false;
		for (uint num2 = 1u; num2 <= num; num2++)
		{
			foreach (uint item in value.StageQuestIds((int)num2))
			{
				if (!GameData.ActivityQuestData.TryGetValue(item, out ActivityQuestExcel value2))
				{
					continue;
				}
				PlayerAttr orCreate = player.Attributes.GetOrCreate(7u, item);
				if (GetProgress(orCreate.Val) < value2.CheckValue)
				{
					orCreate.Val = Pack(value2.CheckValue, IsReceived(orCreate.Val));
					if (sync != null)
					{
						player.Attributes.SyncTo(sync, orCreate);
					}
					flag = true;
				}
			}
		}
		if (flag)
		{
			Save(player);
		}
	}

	private static uint FindStage(PlayerReturnRewardExcel rewardConf, uint questId)
	{
		for (int i = 1; i <= rewardConf.StageCount; i++)
		{
			if (rewardConf.StageQuestIds(i).Contains(questId))
			{
				return (uint)i;
			}
		}
		return 0u;
	}

	private static uint PackSignDays(uint signed, uint claimed)
	{
		return ((claimed & 0xFF) << 8) | (signed & 0xFF);
	}

	private static uint GetProgress(uint raw)
	{
		return (raw >> 1) & 0x7FFFFFFF;
	}

	private static bool IsReceived(uint raw)
	{
		return (raw & 1) != 0;
	}

	private static uint Pack(uint progress, bool received)
	{
		return (Math.Min(progress, 2147483647u) << 1) | (uint)(received ? 1 : 0);
	}

	private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(gid, sid);
		if (orCreate.Val != value)
		{
			orCreate.Val = value;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
		}
	}

	private static void Save(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
	}
}
