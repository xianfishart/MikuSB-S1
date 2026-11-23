using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class DlcEraGuards
{
	private static readonly char[] ForbiddenPatchChars = new char[6] { '+', '<', '>', '&', '\'', '"' };

	private static readonly object LogGate = new object();

	private static readonly int[] Delays = new int[5] { 4000, 11000, 24000, 45000, 75000 };

	private const string RepressionFix = "local RC=rawget(_G,[[Repression_Chapter]])\nif type(RC)~=[[table]] then print([[NORC3]]) return end\nif rawget(RC,[[__RPV]])==1 then print([[RPALREADY2]]) return end\nlocal og=rawget(RC,[[__RPO]]) or rawget(RC,[[GetOpenVersion]])\nif type(og)~=[[function]] then print([[RPNOGO]]) return end\nrawset(RC,[[__RPO]],og)\nrawset(RC,[[__RPV]],1)\nrawset(RC,[[GetOpenVersion]],function()\n  local now=GetTime()\n  local best=-1\n  for id,act in pairs(RC.tbActivity) do\n    if type(id)==[[number]] and type(act)==[[table]] and type(act.OpenTime)==[[number]] and type(act.CloseTime)==[[number]] and act.OpenTime ~= 0 then\n      if math.max(act.CloseTime,now)==act.CloseTime and math.max(act.OpenTime,now)==now then\n        if math.max(id,best)==id then best=id end\n      end\n    end\n  end\n  if best ~= -1 then return best end\n  return og()\nend)\nlocal m=package.loaded[ [[UMG.Dlc8.umg_dlc8_main]] ]\nif m and type(m.tbBtnInfo)==[[table]] and type(m.tbBtnInfo.Repression)==[[table]] then\n  m.tbBtnInfo.Repression.SystemUnlock=true\nend\nfor _,w in pairs(UI.tbWidget) do\n  if type(w)==[[table]] and w.sName==[[dlc8main]] and type(w.tbBtnInfo)==[[table]] and type(w.tbBtnInfo.Repression)==[[table]] then\n    w.tbBtnInfo.Repression.SystemUnlock=true\n    w.tbBtnInfo.Repression.bLock=false\n    w.tbBtnInfo.Repression.bOver=false\n  end\nend\nprint([[RPOK3 v=]]..tostring(RC.GetOpenVersion()))";

	private const string Dlc8Fix = "local RC=rawget(_G,[[Repression_Chapter]])\nlocal m=package.loaded[ [[UMG.Dlc8.umg_dlc8_main]] ]\nif type(RC)~=[[table]] or type(m)~=[[table]] then print([[NODL8]]) return end\nlocal now=GetTime()\nlocal far=now-(-315360000)\nlocal fixed=0\nfor vi,tb in pairs(RC.tbChapter) do\n  if type(tb)==[[table]] then\n    for dc,br in pairs(tb) do\n      if type(br)==[[table]] then\n        for bi,row in pairs(br) do\n          if type(row)==[[table]] and type(row.CloseTime)==[[number]] and row.CloseTime==-1 then\n            row.CloseTime=far\n            row.OpenTime=0\n            fixed=fixed-(-1)\n          end\n        end\n      end\n    end\n  end\nend\nif rawget(m,[[__ONLV]])==1 then print([[DL8ALREADY f=]]..tostring(fixed)) return end\nlocal og=rawget(m,[[__ONLO]]) or rawget(m,[[InitTimeInfo]])\nif type(og)~=[[function]] then print([[DL8NOGO]]) return end\nrawset(m,[[__ONLO]],og)\nrawset(m,[[__ONLV]],1)\nrawset(m,[[InitTimeInfo]],function(self)\n  if self.tbConf==nil then self.tbConf=DLC_Logic.GetCurConf() end\n  local r1,r2=pcall(og,self)\n  local bi=self.tbBtnInfo and self.tbBtnInfo.Online\n  if type(bi)==[[table]] then\n    bi.SystemUnlock=true\n    bi.bLock=false\n    bi.bOver=false\n  end\n  if not r1 then print([[DL8FIX err=]]..tostring(r2):sub(1,100)) end\n  return r1\nend)\nprint([[DL8OK1 f=]]..tostring(fixed))";

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
					await Push(connection, $"login@{ms}");
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					Log("push failed: " + ex2.Message);
				}
			});
		}
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
					Log("resend failed: " + ex2.Message);
				}
			});
		}
	}

	public static async Task Push(Connection connection, string reason)
	{
		if (connection.Player == null)
		{
			return;
		}
		uint eraId = DlcPin.Override();
		int sent = 0;
		List<string> skipped = new List<string>();
		foreach (var (name, lua) in Build(eraId))
		{
			if (await Send(connection, name, lua))
			{
				sent++;
			}
			else
			{
				skipped.Add(name);
			}
		}
		Log($"{reason}: 第 {eraId} 期守护补丁 {sent}/2" + ((skipped.Count > 0) ? (" 跳过[" + string.Join(",", skipped) + "]") : ""));
	}

	private static List<(string Name, string Lua)> Build(uint eraId)
	{
		return new List<(string, string)>
		{
			("RepressionFix", "local RC=rawget(_G,[[Repression_Chapter]])\nif type(RC)~=[[table]] then print([[NORC3]]) return end\nif rawget(RC,[[__RPV]])==1 then print([[RPALREADY2]]) return end\nlocal og=rawget(RC,[[__RPO]]) or rawget(RC,[[GetOpenVersion]])\nif type(og)~=[[function]] then print([[RPNOGO]]) return end\nrawset(RC,[[__RPO]],og)\nrawset(RC,[[__RPV]],1)\nrawset(RC,[[GetOpenVersion]],function()\n  local now=GetTime()\n  local best=-1\n  for id,act in pairs(RC.tbActivity) do\n    if type(id)==[[number]] and type(act)==[[table]] and type(act.OpenTime)==[[number]] and type(act.CloseTime)==[[number]] and act.OpenTime ~= 0 then\n      if math.max(act.CloseTime,now)==act.CloseTime and math.max(act.OpenTime,now)==now then\n        if math.max(id,best)==id then best=id end\n      end\n    end\n  end\n  if best ~= -1 then return best end\n  return og()\nend)\nlocal m=package.loaded[ [[UMG.Dlc8.umg_dlc8_main]] ]\nif m and type(m.tbBtnInfo)==[[table]] and type(m.tbBtnInfo.Repression)==[[table]] then\n  m.tbBtnInfo.Repression.SystemUnlock=true\nend\nfor _,w in pairs(UI.tbWidget) do\n  if type(w)==[[table]] and w.sName==[[dlc8main]] and type(w.tbBtnInfo)==[[table]] and type(w.tbBtnInfo.Repression)==[[table]] then\n    w.tbBtnInfo.Repression.SystemUnlock=true\n    w.tbBtnInfo.Repression.bLock=false\n    w.tbBtnInfo.Repression.bOver=false\n  end\nend\nprint([[RPOK3 v=]]..tostring(RC.GetOpenVersion()))"),
			("Dlc8Fix", "local RC=rawget(_G,[[Repression_Chapter]])\nlocal m=package.loaded[ [[UMG.Dlc8.umg_dlc8_main]] ]\nif type(RC)~=[[table]] or type(m)~=[[table]] then print([[NODL8]]) return end\nlocal now=GetTime()\nlocal far=now-(-315360000)\nlocal fixed=0\nfor vi,tb in pairs(RC.tbChapter) do\n  if type(tb)==[[table]] then\n    for dc,br in pairs(tb) do\n      if type(br)==[[table]] then\n        for bi,row in pairs(br) do\n          if type(row)==[[table]] and type(row.CloseTime)==[[number]] and row.CloseTime==-1 then\n            row.CloseTime=far\n            row.OpenTime=0\n            fixed=fixed-(-1)\n          end\n        end\n      end\n    end\n  end\nend\nif rawget(m,[[__ONLV]])==1 then print([[DL8ALREADY f=]]..tostring(fixed)) return end\nlocal og=rawget(m,[[__ONLO]]) or rawget(m,[[InitTimeInfo]])\nif type(og)~=[[function]] then print([[DL8NOGO]]) return end\nrawset(m,[[__ONLO]],og)\nrawset(m,[[__ONLV]],1)\nrawset(m,[[InitTimeInfo]],function(self)\n  if self.tbConf==nil then self.tbConf=DLC_Logic.GetCurConf() end\n  local r1,r2=pcall(og,self)\n  local bi=self.tbBtnInfo and self.tbBtnInfo.Online\n  if type(bi)==[[table]] then\n    bi.SystemUnlock=true\n    bi.bLock=false\n    bi.bOver=false\n  end\n  if not r1 then print([[DL8FIX err=]]..tostring(r2):sub(1,100)) end\n  return r1\nend)\nprint([[DL8OK1 f=]]..tostring(fixed))")
		};
	}

	private static async Task<bool> Send(Connection connection, string name, string lua)
	{
		char[] forbiddenPatchChars = ForbiddenPatchChars;
		foreach (char ch in forbiddenPatchChars)
		{
			if (lua.IndexOf(ch) >= 0)
			{
				Log($"补丁 {name} 含禁用字符 {ch}，丢弃（客户端 Json 解不了这种转义）");
				return false;
			}
		}
		string payload = new JsonObject { ["code"] = lua }.ToJsonString();
		await CallGSRouter.SendScript(connection, "GM", payload);
		return true;
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "dlcguards.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
