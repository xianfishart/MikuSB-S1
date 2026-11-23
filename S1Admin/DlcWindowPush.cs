using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class DlcWindowPush
{
	public const long WindowBeforeSeconds = 3600L;

	public const long WindowAfterSeconds = 2592000L;

	private static readonly object LogGate = new object();

	public static (long Start, long End) Window()
	{
		long num = DateTimeOffset.Now.ToUnixTimeSeconds();
		return (Start: num - 3600, End: num + 2592000);
	}

	public static DlcActivityExcel? Pinned()
	{
		// 轻量复刻期（1~4）只换卡池和活动列表，DLC 窗口仍按默认期下发。
		uint num = EraLite.DlcEra(DlcPin.Override());
		DlcActivityExcel value;
		return (num != 0 && GameData.DlcActivityData.TryGetValue(num, out value)) ? value : null;
	}

	public static void OnLogin(Connection connection)
	{
		PushSoon(connection, "login", 1500);
	}

	public static void PushSoon(Connection connection, string reason, int delayMs = 0)
	{
		Task.Run(async () =>
		{
			try
			{
				if (delayMs > 0)
				{
					await Task.Delay(delayMs);
				}
				await Push(connection, reason);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Log(reason + " push failed: " + ex2.Message);
			}
		});
	}

	public static void PushAllOnline(string reason)
	{
		foreach (KeyValuePair<long, SocketConnection> item in SocketListener.Connections.ToList())
		{
			SocketConnection value = item.Value;
			Connection conn = value as Connection;
			if (conn == null || conn.Player == null)
			{
				continue;
			}
			Task.Run(async () =>
			{
				try
				{
					await Push(conn, reason);
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log($"push uid={conn.Player?.Uid} failed: {ex2.Message}");
				}
			});
		}
	}

	public static async Task Push(Connection connection, string reason)
	{
		if (connection.Player != null)
		{
			DlcActivityExcel era = Pinned();
			if (era == null)
			{
				Log(reason + ": 未 pin 版本，跳过开放时间下发");
				return;
			}
			(long, long) tuple = Window();
			long start = tuple.Item1;
			long end = tuple.Item2;
			List<uint> chapters = await PushChapter(connection, era, start, end);
			int shops = await PushShop(connection, era, start, end);
			int defend = await PushDefend(connection, era, start, end);
			int rush = await PushRush(connection, era, start, end);
			int online = await PushOnlineWindow(connection, era, start, end);
			int dorm = await PushDorm(connection, era);
			int boss = await PushSeasonBoss(connection, era, start, end);
			Log($"{reason}: 第 {era.Id} 期 章节[{string.Join(",", chapters)}] 商店 {shops} 个 防守 {defend} 个 限时 {rush} 个 联机对齐 {online} 项 宿舍 {dorm} 项 赛季首领 {boss} 个");
		}
	}

	private static async Task<List<uint>> PushChapter(Connection connection, DlcActivityExcel era, long start, long end)
	{
		List<uint> chapters = new List<uint>();
		uint refresh = ResolveFunc(era.DlcFuncConfId, "Refresh");
		if (refresh != 0)
		{
			chapters.Add(refresh);
		}
		if (era.UnlockChapter != 0)
		{
			chapters.Add(era.UnlockChapter);
		}
		uint story = ResolveFunc(era.DlcFuncConfId, "Story");
		if (story != 0)
		{
			chapters.Add(story);
		}
		chapters = chapters.Distinct().ToList();
		if (chapters.Count == 0)
		{
			return chapters;
		}
		JsonArray rows = new JsonArray();
		foreach (uint id in chapters)
		{
			rows.Add(new JsonObject
			{
				["nID"] = id,
				["OpenTime"] = start,
				["CloseTime"] = end
			});
		}
		await CallGSRouter.SendScript(connection, "DLCChapter_SyncTime", rows.ToJsonString());
		return chapters;
	}

	private static async Task<int> PushShop(Connection connection, DlcActivityExcel era, long start, long end)
	{
		// 第 235 节（2026-11-11）：只推本期 DLC 配置里的 ShopIds（第 7 期是 113/114）。
		// 原来这里还 AddRange(GameData.DlcShopData.Keys)，把全部 58 个 DLC 商店
		//（含追忆摊位 401~404）一律推成「现在开放」，导致追忆商店抢在当期商店前面
		// 变成页签。活动商店统一走原生后，追忆商店整体关闭：不推时间，让它们
		// 继续沿用 Resources/shop/shop_tab.json 里的原始（已过期）时间，自然不开放。
		List<uint> ids = new List<uint>(era.ShopIds);
		ids = ids.Distinct().ToList();
		if (ids.Count == 0)
		{
			return 0;
		}
		JsonObject payload = new JsonObject();
		foreach (uint item in ids)
		{
			payload[item.ToString()] = new JsonObject
			{
				["nBegin"] = start,
				["nEnd"] = end
			};
		}
		await CallGSRouter.SendScript(connection, "ShopLogic_GetOpenTime", payload.ToJsonString());
		return ids.Count;
	}

	private static async Task<int> PushDefend(Connection connection, DlcActivityExcel era, long start, long end)
	{
		int count = 0;
		foreach (JsonObject row in ReadResource("challenge/defend/defend_act.json"))
		{
			if (ReadUInt(row["DlcId"]) == era.Id)
			{
				uint id = ReadUInt(row["ID"]);
				if (id != 0)
				{
					JsonObject payload = new JsonObject
					{
						["bOpen"] = true,
						["nId"] = id,
						["nBeginTime"] = start,
						["nEndTime"] = end
					};
					await CallGSRouter.SendScript(connection, "DefendLogic_CheckOpenAct", payload.ToJsonString());
					count++;
				}
			}
		}
		return count;
	}

	/// <summary>
	/// 第 493 节：DlcFuncConfId 里 Rush 填的可能是「活动 Id」而不是「组号」。
	/// 第 27 期 Rush=71，而 online.json 里 Id=71 那一行的 Group 是 70 —— 直接拿 71 当组号筛选
	/// 一行都命中不了，服务端于是从来不给派对推开放时间；客户端 Rush 功能对象也是拿这个值
	/// 当组号去 Online.GetGroupConfig 的，取不到就一路判成「暂未开放」。
	/// 组号直查不到时，按 Id 反查它所在的组号。
	/// </summary>
	private static uint ResolveRushGroup(uint rush, List<JsonObject> onlineRows)
	{
		if (rush == 0)
		{
			return 0;
		}
		if (onlineRows.Any((JsonObject r) => ReadUInt(r["Group"]) == rush))
		{
			return rush;
		}
		uint group = onlineRows.Where((JsonObject r) => ReadUInt(r["Id"]) == rush).Select((JsonObject r) => ReadUInt(r["Group"])).FirstOrDefault();
		if (group == 0)
		{
			return rush;
		}
		return group;
	}

	private static async Task<int> PushRush(Connection connection, DlcActivityExcel era, long start, long end)
	{
		List<JsonObject> onlineRows = ReadResource("online/online.json");
		uint group = ResolveRushGroup(ResolveFunc(era.DlcFuncConfId, "Rush"), onlineRows);
		if (group == 0)
		{
			return 0;
		}
		int count = 0;
		foreach (JsonObject row in onlineRows)
		{
			if (ReadUInt(row["Group"]) == group)
			{
				uint id = ReadUInt(row["Id"]);
				if (id != 0)
				{
					JsonObject payload = new JsonObject { ["tbConfig"] = new JsonObject
					{
						["nId"] = id,
						["nStartTime"] = start,
						["nEndTime"] = end,
						["tbCondition"] = new JsonObject(),
						["tbOpenHour"] = new JsonArray(),
						["tbDate"] = new JsonArray()
					} };
					await CallGSRouter.SendScript(connection, "Online_GMChangeTime", payload.ToJsonString());
					count++;
				}
			}
		}
		return count;
	}

	private static async Task<int> PushOnlineWindow(Connection connection, DlcActivityExcel era, long start, long end)
	{
		long eraLo = ParseTime(era.EnterStartTime);
		long eraHi = ParseTime(era.CloseEndTime);
		if (eraLo == 0L || eraHi == 0)
		{
			return 0;
		}
		List<JsonObject> onlineRows = ReadResource("online/online.json");
		uint rushGroup = ResolveRushGroup(ResolveFunc(era.DlcFuncConfId, "Rush"), onlineRows);
		long now = DateTimeOffset.Now.ToUnixTimeSeconds();
		List<uint> allowed = new List<uint>();
		int count = 0;
		foreach (JsonObject row in onlineRows)
		{
			uint id = ReadUInt(row["Id"]);
			if (id == 0)
			{
				continue;
			}
			long lo = ParseTime(row["StartTime"]?.ToString());
			long hi = ParseTime(row["EndTime"]?.ToString());
			bool inEra = lo != 0L && hi != 0L && lo <= eraHi && hi >= eraLo;
			bool coversNow = lo != 0L && hi != 0L && lo <= now && now <= hi;
			if (inEra)
			{
				allowed.Add(id);
			}
			if (rushGroup != 0 && ReadUInt(row["Group"]) == rushGroup)
			{
				continue;
			}
			long nStart;
			long nEnd;
			if (inEra)
			{
				if (coversNow)
				{
					continue;
				}
				nStart = start;
				nEnd = end;
			}
			else
			{
				if (!coversNow)
				{
					continue;
				}
				nStart = 0L;
				nEnd = 1L;
			}
			JsonObject payload = new JsonObject { ["tbConfig"] = new JsonObject
			{
				["nId"] = id,
				["nStartTime"] = nStart,
				["nEndTime"] = nEnd,
				["tbCondition"] = new JsonObject(),
				["tbOpenHour"] = new JsonArray(),
				["tbDate"] = new JsonArray()
			} };
			await CallGSRouter.SendScript(connection, "Online_GMChangeTime", payload.ToJsonString());
			count++;
		}
		await SendOnlineFilter(connection, era.Id, allowed);
		return count;
	}

	private static async Task SendOnlineFilter(Connection connection, uint eraId, List<uint> allowed)
	{
		string ids = string.Join(",", allowed);
		string lua = "local M=Online\nif M~=nil then\nlocal tbSet=M.tbMikuEraIds\nif type(tbSet)~=[[table]] then tbSet={} M.tbMikuEraIds=tbSet end\nfor k in pairs(tbSet) do tbSet[k]=nil end\nlocal ids={" + ids + "}\nfor _,v in ipairs(ids) do tbSet[v]=true end\nif not M.bMikuEraFilter then\nM.bMikuEraFilter=true\nlocal fnOld=M.GetAllOpenList\nif type(fnOld)==[[function]] then\nM.GetAllOpenList=function(...)\nlocal L=fnOld(...)\nif type(L)~=[[table]] then return L end\nlocal S=M.tbMikuEraIds\nif type(S)~=[[table]] then return L end\nlocal n=0\nfor _ in pairs(S) do n=n-(-1) end\nif n==0 then return L end\nlocal out={}\nfor _,v in ipairs(L) do\nif type(v)==[[table]] then\nif S[v.nId] or S[v.nGroup] then out[#out-(-1)]=v end\nend\nend\nlocal i=1\nwhile i~=#out do\nlocal j=i-(-1)\nwhile j~=1 do\nlocal d=out[j].nId-out[j-1].nId\nif d~=0 and math.abs(d)~=d then\nout[j],out[j-1]=out[j-1],out[j]\nj=j-1\nelse\nj=1\nend\nend\ni=i-(-1)\nend\nreturn out\nend\nend\nend\nend";
		char[] array = new char[6] { '+', '<', '>', '&', '\'', '"' };
		foreach (char ch in array)
		{
			if (lua.IndexOf(ch) >= 0)
			{
				Log($"联机过滤器含禁用字符 {ch}，跳过（第 {eraId} 期）");
				return;
			}
		}
		string payload = new JsonObject { ["code"] = lua }.ToJsonString();
		await CallGSRouter.SendScript(connection, "GM", payload);
		Log($"第 {eraId} 期 联机白名单 {allowed.Count} 项: {ids}");
	}

	private static long ParseTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0L;
		}
		string text = raw.Trim().Trim(new char[4] { '[', ']', '"', ' ' });
		if (text.Length < 12)
		{
			return 0L;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTimeOffset(result, TimeZoneInfo.Local.GetUtcOffset(result)).ToUnixTimeSeconds() : 0;
	}

	private static async Task<int> PushDorm(Connection connection, DlcActivityExcel era)
	{
		uint actId = ResolveFunc(era.DlcFuncConfId, "Dorm");
		if (actId == 0)
		{
			return 0;
		}
		if (!GameData.RoleFestivalMessageData.ContainsKey(actId))
		{
			return 0;
		}
		await CallGSRouter.SendScript(connection, "GMRoleFestivalSetId", new JsonObject { ["nActId"] = actId }.ToJsonString());
		return 1;
	}

	private static async Task<int> PushSeasonBoss(Connection connection, DlcActivityExcel era, long start, long end)
	{
		uint actId = ResolveFunc(era.DlcFuncConfId, "Teamboss");
		if (actId == 0)
		{
			return 0;
		}
		JsonArray terms = new JsonArray();
		foreach (JsonObject row in ReadResource("challenge/seasonboss/seasonlist.json"))
		{
			if (ReadUInt(row["ActID"]) == actId)
			{
				uint id = ReadUInt(row["Id"]);
				if (id != 0)
				{
					terms.Add(new JsonObject
					{
						["nID"] = id,
						["nStartTime"] = start,
						["nEndTime"] = end,
						["nRewardTime"] = end
					});
				}
			}
		}
		if (terms.Count == 0)
		{
			return 0;
		}
		await CallGSRouter.SendScript(connection, "SeasonBoss_GMChangeTime", new JsonObject { ["tbTerm"] = terms }.ToJsonString());
		return terms.Count;
	}

	public static uint ResolveFunc(string? dlcFuncConfId, string funcName)
	{
		if (string.IsNullOrWhiteSpace(dlcFuncConfId))
		{
			return 0u;
		}
		Match match = Regex.Match(dlcFuncConfId, "(?:^|[{,\\s])" + Regex.Escape(funcName) + "\\s*=\\s*(\\d+)");
		uint result;
		return (match.Success && uint.TryParse(match.Groups[1].Value, out result)) ? result : 0u;
	}

	private static List<JsonObject> ReadResource(string relative)
	{
		string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", relative.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(path))
		{
			return new List<JsonObject>();
		}
		try
		{
			return (JsonNode.Parse(File.ReadAllText(path)) is JsonArray source) ? source.OfType<JsonObject>().ToList() : new List<JsonObject>();
		}
		catch
		{
			return new List<JsonObject>();
		}
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue))
		{
			return 0u;
		}
		if (jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		if (jsonValue.TryGetValue<int>(out var value2))
		{
			return (uint)Math.Max(0, value2);
		}
		if (jsonValue.TryGetValue<long>(out var value3))
		{
			return (uint)Math.Max(0L, value3);
		}
		if (jsonValue.TryGetValue<double>(out var value4))
		{
			return (uint)Math.Max(0.0, value4);
		}
		if (jsonValue.TryGetValue<string>(out string value5))
		{
			string s = value5.Trim().Trim(new char[3] { '[', ']', '"' });
			uint result;
			return uint.TryParse(s, out result) ? result : 0u;
		}
		return 0u;
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "dlcwindow.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
