using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_CheckOpenAct")]
public class VirCapture_CheckOpenAct : ICallGSHandler
{
	private const uint GroupId = 128u;

	private const uint ActIdSid = 1u;

	private const uint CurLevelSid = 3u;

	private const uint TrialActIdSid = 6u;

	private const uint SeasonActIdSid = 9u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		DateTime now = DateTime.Now;
		uint requested = 0u;
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			double d = default;
			int num;
			if (jsonNode is JsonObject req)
			{
				jsonNode = req["nId"];
				if (jsonNode is JsonValue value && value.TryGetValue<double>(out d))
				{
					num = ((d > 0.0) ? 1 : 0);
					goto IL_00d3;
				}
			}
			num = 0;
			goto IL_00d3;
			IL_00d3:
			if (num != 0)
			{
				requested = (uint)d;
			}
		}
		catch
		{
		}
		VirCaptureTimeExcel act = ResolveCurrent(GameData.VirCaptureTimeData.Values, now);
		if (requested != 0)
		{
			VirCaptureTimeExcel byId = GameData.VirCaptureTimeData.Values.FirstOrDefault((VirCaptureTimeExcel x) => x.Id == requested);
			if (byId != null)
			{
				act = byId;
			}
		}
		if (act == null)
		{
			await CallGSRouter.SendScript(connection, "VirCapture_CheckOpenAct", "{\"bOpen\":false}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		SetAttr(player, 1u, act.Id, sync);
		EnsureMinAttr(player, 3u, 1u, sync);
		JsonObject response = new JsonObject
		{
			["bOpen"] = true,
			["nId"] = act.Id,
			["nStartTime"] = ToUnixSeconds(ParseConfigTime(act.StartTime)),
			["nEndTime"] = ToUnixSeconds(ParseConfigTime(act.EndTime))
		};
		VirCaptureSeasonExcel season = ResolveCurrent(GameData.VirCaptureSeasonData.Values, now);
		if (season != null)
		{
			SetAttr(player, 9u, season.Id, sync);
			response["tbSeason"] = new JsonObject
			{
				["nId"] = season.Id,
				["nStartTime"] = ToUnixSeconds(ParseConfigTime(season.StartTime)),
				["nEndTime"] = ToUnixSeconds(ParseConfigTime(season.EndTime))
			};
		}
		else
		{
			SetAttr(player, 9u, 0u, sync);
		}
		SetAttr(player, 6u, ResolveCurrent(GameData.VirCaptureTrialTimeData.Values, now)?.Id ?? 0, sync);
		// 第 497 节：蓝图岛任务的引导占位条目（vircapture_task.json 里文案/奖励全空的 100 号）
		// 必须由服务端置成「已领」，否则主线 101 的 IsPreFinished 永远 false，整条主线在客户端不显示。
		// 第 502 节：顺带把「建筑已经建好」的 Type 2 主线任务补完成（见 SyncBuildTasks）。
		VirCaptureBuildTaskState.SyncAll(player, sync);
		// 第 511 节：Type 7「送达型」任务（122/230/328 送员工）没有卡面，玩家点不到领取，
		// 所以登录/打开新星开拓时代领一次，员工直接写进 162，员工列表里马上能看到。
		await VirCaptureBuildTaskState.ClaimTriggerTasksAsync(player, 0u, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "VirCapture_CheckOpenAct", response.ToJsonString(), sync);
	}

	private static T? ResolveCurrent<T>(IEnumerable<T> configs, DateTime now) where T : class
	{
		var source = (from x in configs
			select new
			{
				Config = x,
				Start = ParseConfigTime(GetTimeValue(x, start: true)),
				End = ParseConfigTime(GetTimeValue(x, start: false))
			} into x
			where x.Start.HasValue && x.End.HasValue
			orderby x.Start
			select x).ToList();
		var anon = source.FirstOrDefault(x =>
		{
			int result;
			if (x.Start <= now)
			{
				DateTime value = now;
				DateTime? end = x.End;
				result = ((value < end) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		});
		if (anon != null)
		{
			return anon.Config;
		}
		var anon2 = source.LastOrDefault(x => x.Start <= now);
		if (anon2 != null && anon2.End > anon2.Start)
		{
			return anon2.Config;
		}
		return null;
	}

	private static string? GetTimeValue<T>(T value, bool start) where T : class
	{
		if (1 == 0)
		{
		}
		string result;
		if (value is VirCaptureTimeExcel virCaptureTimeExcel)
		{
			result = (start ? virCaptureTimeExcel.StartTime : virCaptureTimeExcel.EndTime);
		}
		else if (value is VirCaptureSeasonExcel virCaptureSeasonExcel)
		{
			result = (start ? virCaptureSeasonExcel.StartTime : virCaptureSeasonExcel.EndTime);
		}
		else if (value is VirCaptureTrialTimeExcel virCaptureTrialTimeExcel)
		{
			// 第 351 节：试炼（试炼吧）时间表原来没有分支，GetTimeValue 一律返回 null，
			// ResolveCurrent 就把 trial_timelist 全过滤光 → 128/6 永远写不进去（0），
			// 客户端看不到「试炼吧」这一期。补上后与活动/赛季同一套时间口径。
			result = (start ? virCaptureTrialTimeExcel.StartTime : virCaptureTrialTimeExcel.EndTime);
		}
		else
		{
			result = null;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}

	private static long ToUnixSeconds(DateTime? value)
	{
		return value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : 0;
	}

	private static void EnsureMinAttr(PlayerInstance player, uint sid, uint minValue, NtfSyncPlayer sync)
	{
		PlayerAttr orCreateAttr = GetOrCreateAttr(player, sid);
		if (orCreateAttr.Val < minValue)
		{
			orCreateAttr.Val = minValue;
			SyncAttr(player, sync, sid, orCreateAttr.Val);
		}
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreateAttr = GetOrCreateAttr(player, sid);
		if (orCreateAttr.Val != value)
		{
			orCreateAttr.Val = value;
			SyncAttr(player, sync, sid, value);
		}
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = 128u,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		sync.Custom[player.ToPackedAttrKey(128u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(128u, sid)] = value;
	}
}
