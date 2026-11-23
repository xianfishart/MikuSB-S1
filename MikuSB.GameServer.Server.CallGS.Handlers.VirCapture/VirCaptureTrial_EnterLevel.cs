using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「试炼吧」开始作战（第 352 节）。
///
/// 客户端点「开始作战」发的就是 VirCaptureTrial_EnterLevel；服务端原来没有这个接口，
/// CallGSRouter 走兜底 SendUnavailableResponse → {"sErr":"ui.TxtNotOpen"}，
/// 客户端提示「暂未开放」。补上之后按「校验时间窗 + 回 seed」处理。
///
/// 参数名容错：客户端 lua 是 vm 混淆过的，字段名无法从反汇编确认，所以这里同时接受
/// nID / nId / nLevelID / nLevelId / nTrialID / nMapID / ID，取第一个非 0 的。
/// </summary>
[CallGSApi("VirCaptureTrial_EnterLevel")]
public class VirCaptureTrial_EnterLevel : ICallGSHandler
{
	private static readonly Logger Logger = new Logger("VirCaptureTrial");

	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject req = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint trialId = ReadUint(req, "nID", "nId", "nLevelID", "nLevelId", "nTrialID", "nTrialId", "nMapID", "ID", "id");
		int teamId = (int)ReadUint(req, "nTeamID", "nTeamId", "nTeam", "nTeamIDList");
		if (trialId == 0)
		{
			Logger.Error("VirCaptureTrial_EnterLevel bad param: " + param);
			await CallGSRouter.SendScript(connection, "VirCaptureTrial_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		// 时间窗只做记录，不拦截：试炼期数已由第 351 节改成 2099 年长期开放，
		// 真要收紧再改成返回 ui.TxtNotOpen。拦错了会导致「活动明明开着却进不去」。
		VirCaptureTrialTimeExcel act = ResolveCurrent(GameData.VirCaptureTrialTimeData.Values, DateTime.Now);
		Logger.Info($"VirCaptureTrial_EnterLevel uid={connection.Player.Uid} trialId={trialId} teamId={teamId} actId={act?.Id.ToString() ?? "null"} raw={param}");

		int seed = Random.Next(1, 1000000000);
		string rsp = "{\"nSeed\":" + seed + ",\"nID\":" + trialId + "}";
		await CallGSRouter.SendScript(connection, "VirCaptureTrial_EnterLevel", rsp);
	}

	private static uint ReadUint(JsonObject obj, params string[] keys)
	{
		foreach (string key in keys)
		{
			JsonNode node = obj[key];
			if (node == null)
			{
				continue;
			}
			if (node is JsonValue value && value.TryGetValue<uint>(out var direct))
			{
				return direct;
			}
			if (uint.TryParse(node.ToString(), out var parsed))
			{
				return parsed;
			}
		}
		return 0u;
	}

	private static VirCaptureTrialTimeExcel? ResolveCurrent(IEnumerable<VirCaptureTrialTimeExcel> configs, DateTime now)
	{
		var source = (from x in configs
			select new
			{
				Config = x,
				Start = ParseConfigTime(x.StartTime),
				End = ParseConfigTime(x.EndTime)
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
}
