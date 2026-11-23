using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class ChapterCard
{
	private static readonly object LogGate = new object();

	private static readonly int[] Delays = new int[3] { 4000, 10000, 20000 };

	public static void OnLogin(Connection connection)
	{
		int[] delays = Delays;
		foreach (int num in delays)
		{
			int ms = num;
			Task.Run(async () =>
			{
				try
				{
					await Task.Delay(ms);
					Push(connection);
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log("push failed: " + ex2.Message);
				}
			});
		}
	}

	public static void PushAllOnline()
	{
		foreach (KeyValuePair<long, SocketConnection> connection2 in SocketListener.Connections)
		{
			if (connection2.Value is Connection { Player: not null } connection)
			{
				try
				{
					Push(connection);
				}
				catch (Exception ex)
				{
					Log("resend failed: " + ex.Message);
				}
			}
		}
	}

	private static void Push(Connection connection)
	{
		if (connection.Player == null)
		{
			return;
		}
		try
		{
			Send(connection, BuildFix());
		}
		catch (Exception ex)
		{
			Log("send failed: " + ex.Message);
		}
	}

	private static string BuildFix()
	{
		List<string> values = new List<string>
		{
			"local function __cc()", "if not (Chapter and Text and UI and UI.tbWidget) then return end", "local lv", "do local o,v = pcall(Chapter.GetProceedNotPlot) if o and type(v)==[[number]] and v~=0 then lv=v end end", "if not lv then do local o,v = pcall(Chapter.GetProceedLevel) if o and type(v)==[[number]] and v~=0 then lv=v end end end", "if not lv then return end", "local cfg", "do local o,v = pcall(Chapter.GetChapterCfgByLevelID, lv) if o then cfg=v end end", "if type(cfg) ~= [[table]] then return end", "local main",
			"for _,w in pairs(UI.tbWidget) do if type(w)==[[table]] and w.sName==[[main]] then main=w end end", "if not main then return end", "local o = main.Object", "if type(o) ~= [[table]] then return end", "local cid = tonumber(cfg.nID) or 0", "local nm = [[]]", "if cid ~= 0 then do local k,v = pcall(Text, [[chapter.main_]]..tostring(cid)) if k and type(v)==[[string]] then nm=v end end end", "if #nm == 0 and type(cfg.sName)==[[string]] then nm=cfg.sName end", "if #nm ~= 0 and o.TxtFightName then pcall(function() o.TxtFightName:SetText(nm) end) end", "local a,b = [[]],[[]]",
			"do local k,v = pcall(Text, [[chapter.level_]]..tostring(lv)) if k and type(v)==[[string]] then a=v end end", "do local k,v = pcall(Text, [[chapter.level_name_]]..tostring(lv)) if k and type(v)==[[string]] then b=v end end", "local lt = a", "if #b ~= 0 then if #lt == 0 then lt=b else lt=lt..[[ ]]..b end end", "if #lt ~= 0 and o.TxtFightLevel then pcall(function() o.TxtFightLevel:SetText(lt) end) end", "end", "pcall(__cc)"
		};
		return string.Join(" ", values);
	}

	private static void Send(Connection conn, string lua)
	{
		string arg = new JsonObject { ["code"] = lua }.ToJsonString();
		CallGSRouter.SendScript(conn, "GM", arg).GetAwaiter().GetResult();
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "chaptercard.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
