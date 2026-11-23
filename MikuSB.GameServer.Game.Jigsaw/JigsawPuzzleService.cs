using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Jigsaw;

public static class JigsawPuzzleService
{
	private const uint StateMask = 7u;

	public static JigsawScheduleExcel? FindSchedule(uint scheduleId)
	{
		return GameData.JigsawScheduleData.GetValueOrDefault(scheduleId);
	}

	public static JigsawPieceExcel? FindPiece(uint scheduleId, uint pieceId)
	{
		return GameData.JigsawPieceData.GetValueOrDefault(scheduleId * 1000 + pieceId);
	}

	public static IReadOnlyList<JigsawPieceExcel> PiecesOf(uint scheduleId)
	{
		return (from piece in GameData.JigsawPieceData.Values
			where piece.ScheduleId == scheduleId
			orderby piece.Position
			select piece).ToList();
	}

	private static uint StateSid(uint scheduleId, uint pieceId)
	{
		uint num = pieceId - 1;
		return 100 + (scheduleId - 1) * 5 + num / 10;
	}

	private static int StateShift(uint pieceId)
	{
		return (int)((pieceId - 1) % 10 * 3);
	}

	public static uint GetPieceState(PlayerInstance player, uint scheduleId, uint pieceId)
	{
		uint value = player.Attributes.GetValue(34u, StateSid(scheduleId, pieceId));
		return (value >> StateShift(pieceId)) & 7;
	}

	public static void SetPieceState(PlayerInstance player, uint scheduleId, uint pieceId, uint state, NtfSyncPlayer sync)
	{
		uint sid = StateSid(scheduleId, pieceId);
		int num = StateShift(pieceId);
		PlayerAttr orCreate = player.Attributes.GetOrCreate(34u, sid);
		uint num2 = orCreate.Val & (uint)(~(7 << num));
		orCreate.Val = num2 | ((state & 7) << num);
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static uint CountPlaced(PlayerInstance player, uint scheduleId)
	{
		return (uint)Enumerable.Count(PiecesOf(scheduleId), (JigsawPieceExcel piece) => GetPieceState(player, scheduleId, piece.PieceId) == 2);
	}

	public static bool AllPlaced(PlayerInstance player, uint scheduleId)
	{
		IReadOnlyList<JigsawPieceExcel> readOnlyList = PiecesOf(scheduleId);
		return readOnlyList.Count > 0 && readOnlyList.All((JigsawPieceExcel piece) => GetPieceState(player, scheduleId, piece.PieceId) == 2);
	}

	public static uint GetGirlBit(uint scheduleId)
	{
		return (scheduleId <= 30) ? 1u : 2u;
	}

	public static int GetGirlShift(uint scheduleId)
	{
		return (int)((scheduleId > 30) ? (scheduleId - 30) : scheduleId);
	}

	public static bool HasGirl(PlayerInstance player, uint scheduleId)
	{
		return ((player.Attributes.GetValue(34u, GetGirlBit(scheduleId)) >> GetGirlShift(scheduleId)) & 1) != 0;
	}

	public static void SetGirl(PlayerInstance player, uint scheduleId, NtfSyncPlayer sync)
	{
		SetFlag(player, GetGirlBit(scheduleId), GetGirlShift(scheduleId), sync);
	}

	public static bool IsActive(PlayerInstance player, uint scheduleId)
	{
		uint sid = ((scheduleId > 30) ? 4u : 3u);
		int girlShift = GetGirlShift(scheduleId);
		return ((player.Attributes.GetValue(34u, sid) >> girlShift) & 1) != 0;
	}

	public static void SetActive(PlayerInstance player, uint scheduleId, NtfSyncPlayer sync)
	{
		uint sid = ((scheduleId > 30) ? 4u : 3u);
		SetFlag(player, sid, GetGirlShift(scheduleId), sync);
	}

	private static void SetFlag(PlayerInstance player, uint sid, int shift, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(34u, sid);
		orCreate.Val |= (uint)(1 << shift);
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static long GetEnterTime(PlayerInstance player, uint scheduleId)
	{
		return player.Attributes.GetValue(34u, 51 + scheduleId);
	}

	public static void SetEnterTime(PlayerInstance player, uint scheduleId, long now, NtfSyncPlayer? sync)
	{
		if (GetEnterTime(player, scheduleId) <= 0)
		{
			PlayerAttr orCreate = player.Attributes.GetOrCreate(34u, 51 + scheduleId);
			orCreate.Val = (uint)now;
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, orCreate);
			}
		}
	}

	public static long GetHelpTime(PlayerInstance player, uint scheduleId)
	{
		return player.Attributes.GetValue(34u, 10 + scheduleId);
	}

	public static void SetHelpTime(PlayerInstance player, uint scheduleId, long now, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(34u, 10 + scheduleId);
		orCreate.Val = (uint)now;
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static long ActivityStart(uint activityId)
	{
		ActivityExcel valueOrDefault = GameData.ActivityData.GetValueOrDefault(activityId);
		return (valueOrDefault == null) ? 0 : DlcRuntimeOverrides.ToUnixSeconds(DlcRuntimeOverrides.ParseConfigTime(valueOrDefault.StartTime));
	}

	public static long StageTime(PlayerInstance player, uint scheduleId, uint activityId, int stage)
	{
		JigsawScheduleExcel jigsawScheduleExcel = FindSchedule(scheduleId);
		if (jigsawScheduleExcel == null || stage < 1)
		{
			return long.MaxValue;
		}
		long num = ActivityStart(activityId);
		if (num == 0)
		{
			return 0L;
		}
		long enterTime = GetEnterTime(player, scheduleId);
		if (enterTime > 0 && enterTime < num)
		{
			return num;
		}
		IReadOnlyList<uint> phaseTimes = jigsawScheduleExcel.PhaseTimes;
		return num + ((stage <= phaseTimes.Count) ? phaseTimes[stage - 1] : 0);
	}

	public static uint ResolveScheduleForActivity(uint activityId)
	{
		ActivityExcel valueOrDefault = GameData.ActivityData.GetValueOrDefault(activityId);
		if (valueOrDefault == null || !string.Equals(valueOrDefault.Class, "jigsaw_puzzle", StringComparison.Ordinal))
		{
			return 0u;
		}
		if (valueOrDefault.CustomData is JArray { Count: >0 } jArray && jArray[0] is JArray jArray2)
		{
			List<uint> role = new List<uint>();
			foreach (JToken item in jArray2)
			{
				role.Add((uint)((item.Type == JTokenType.Integer) ? ((long)item) : 0u));
			}
			foreach (JigsawScheduleExcel item2 in GameData.JigsawScheduleData.Values.OrderBy((JigsawScheduleExcel s) => s.ScheduleId))
			{
				IReadOnlyList<uint> role2 = item2.Role;
				if (role2.Count != role.Count || role2.Where((uint value, int index) => value != role[index]).Any())
				{
					continue;
				}
				return item2.ScheduleId;
			}
		}
		return 0u;
	}

	public static uint LatestSchedule()
	{
		return (GameData.JigsawScheduleData.Keys.Count != 0) ? GameData.JigsawScheduleData.Keys.Max() : 0u;
	}

	public static void ReportProgress(PlayerInstance player, uint scheduleId, NtfSyncPlayer sync)
	{
		JigsawScheduleExcel jigsawScheduleExcel = FindSchedule(scheduleId);
		if (jigsawScheduleExcel == null)
		{
			return;
		}
		uint checkValue = CountPlaced(player, scheduleId);
		foreach (uint taskId in jigsawScheduleExcel.TaskIds)
		{
			ActivityQuestExcel valueOrDefault = GameData.ActivityQuestData.GetValueOrDefault(taskId);
			if (valueOrDefault == null)
			{
				continue;
			}
			uint checkValue2 = valueOrDefault.CheckValue;
			switch (valueOrDefault.Type)
			{
			case 376u:
				AchievementClaimService.Complete(player, 7u, taskId, checkValue, sync);
				break;
			case 377u:
			{
				List<uint> list = ReadParams(valueOrDefault.Param);
				if (list != null && list.Count >= 2 && PhasePlaced(player, jigsawScheduleExcel, list[1]))
				{
					AchievementClaimService.Complete(player, 7u, taskId, checkValue2, sync);
				}
				break;
			}
			case 378u:
				if (IsActive(player, scheduleId))
				{
					AchievementClaimService.Complete(player, 7u, taskId, checkValue2, sync);
				}
				break;
			}
		}
	}

	private static List<uint> ReadParams(JToken? token)
	{
		List<uint> list = new List<uint>();
		if (!(token is JArray jArray))
		{
			return list;
		}
		foreach (JToken item in jArray)
		{
			if (item.Type == JTokenType.Integer)
			{
				list.Add((uint)(long)item);
			}
		}
		return list;
	}

	private static bool PhasePlaced(PlayerInstance player, JigsawScheduleExcel schedule, uint phase)
	{
		if (phase == 0 || phase > schedule.PhasePieces.Count)
		{
			return false;
		}
		for (int i = 0; i < phase; i++)
		{
			foreach (uint item in schedule.PhasePieces[i])
			{
				if (GetPieceState(player, schedule.ScheduleId, item) != 2)
				{
					return false;
				}
			}
		}
		return true;
	}
}
