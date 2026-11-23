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

public static class MainSurveyHide
{
	private static readonly char[] ForbiddenChars = new char[6] { '+', '<', '>', '&', '\'', '"' };

	private static readonly int[] Delays = new int[11]
	{
		3000, 8000, 15000, 25000, 40000, 60000, 90000, 120000, 180000, 240000,
		300000
	};

	private static readonly object LogGate = new object();

	public const string Lua = "local P=package.loaded\nlocal mk=[[UMG.Main.umg_main]]\nlocal M=P[mk]\nlocal Q=Questionnaire\nif type(M)~=[[table]] then print([[SVNOMOD]]) return end\nlocal o=rawget(M,[[__SVO]])\nif type(o)~=[[function]] then o=rawget(M,[[RefreshUI]]) rawset(M,[[__SVO]],o) end\nlocal L=UE.USlateBlueprintLibrary\nlocal function gnm(w) local ok,n=pcall(function() return w:GetName() end) if ok then return tostring(n) end return [[]] end\nlocal function gw(w) local ok,g=pcall(function() return L.GetLocalSize(w:GetCachedGeometry()) end) if ok and g then return g.X end return 0 end\nlocal function par(w) local ok,p=pcall(function() return w:GetParent() end) if ok then return p end return nil end\nlocal hf=function(self)\nlocal tb=self.tbCacheBtn\nlocal b=nil\nif type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x and gnm(x)==[[BtnSurvey]] then b=x break end end end\nif not b then local x=self.btnSurvey if x then b=x end end\nlocal c=nil local h=nil\nif b then c=par(b) if c then h=par(c) end end\nif not h and type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x then local c2=par(x) local h2=c2 and par(c2) if h2 and gnm(h2)==[[ActivityHorizontal]] then h=h2 break end end end end\nif not h then print([[SVNOH]]) return end\nif b then pcall(function() b:SetVisibility(2) end) end\nif c then pcall(function() c:SetVisibility(2) end) end\nlocal w=gw(h)\nlocal bw=0\nif type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x then local c2=par(x) local h2=c2 and par(c2) if h2 and tostring(h2)==tostring(h) then local okv,v=pcall(function() return c2:GetVisibility() end) if okv and v~=2 then bw=math.max(bw,gw(c2)) end end end end end\nif math.max(0,w)~=0 and math.max(0,bw)~=0 then\nlocal k=math.max(0,math.ceil((w-bw*2.5)/1000000000))\nh:SetRenderTranslation(UE.FVector2D((w/3)*k,0))\nprint([[SVH w=]]..w..[[ bw=]]..bw..[[ k=]]..k)\nend\nend\nlocal f=rawget(M,[[__SVF]])\nif type(f)~=[[function]] or rawget(M,[[__SVV]])~=5 then\nf=function(self,...)\nif Q and type(Q.CurID)==[[number]] and Q.CurID~=0 then Q.CurID=0 end\nlocal r=o(self,...)\nhf(self)\nreturn r\nend\nrawset(M,[[__SVF]],f)\nrawset(M,[[__SVV]],5)\nend\nrawset(M,[[RefreshUI]],f)\nlocal t=nil\nlocal ok,g=pcall(UI.GetTop)\nif ok and type(g)==[[table]] and g.sName==[[main]] then t=g end\nif t and t.RefreshUI~=f then t.RefreshUI=f pcall(hf,t) print([[SVINS]]) end\nif t then hf(t) print([[SVAPPLY]]) end\nif Q and type(Q.CurID)==[[number]] and Q.CurID~=0 then Q.CurID=0 if t then pcall(t.RefreshUI,t) end print([[SVR]]) end\nprint([[SVDONE5]])";

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
		char[] forbiddenChars = ForbiddenChars;
		foreach (char ch in forbiddenChars)
		{
			if ("local P=package.loaded\nlocal mk=[[UMG.Main.umg_main]]\nlocal M=P[mk]\nlocal Q=Questionnaire\nif type(M)~=[[table]] then print([[SVNOMOD]]) return end\nlocal o=rawget(M,[[__SVO]])\nif type(o)~=[[function]] then o=rawget(M,[[RefreshUI]]) rawset(M,[[__SVO]],o) end\nlocal L=UE.USlateBlueprintLibrary\nlocal function gnm(w) local ok,n=pcall(function() return w:GetName() end) if ok then return tostring(n) end return [[]] end\nlocal function gw(w) local ok,g=pcall(function() return L.GetLocalSize(w:GetCachedGeometry()) end) if ok and g then return g.X end return 0 end\nlocal function par(w) local ok,p=pcall(function() return w:GetParent() end) if ok then return p end return nil end\nlocal hf=function(self)\nlocal tb=self.tbCacheBtn\nlocal b=nil\nif type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x and gnm(x)==[[BtnSurvey]] then b=x break end end end\nif not b then local x=self.btnSurvey if x then b=x end end\nlocal c=nil local h=nil\nif b then c=par(b) if c then h=par(c) end end\nif not h and type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x then local c2=par(x) local h2=c2 and par(c2) if h2 and gnm(h2)==[[ActivityHorizontal]] then h=h2 break end end end end\nif not h then print([[SVNOH]]) return end\nif b then pcall(function() b:SetVisibility(2) end) end\nif c then pcall(function() c:SetVisibility(2) end) end\nlocal w=gw(h)\nlocal bw=0\nif type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x then local c2=par(x) local h2=c2 and par(c2) if h2 and tostring(h2)==tostring(h) then local okv,v=pcall(function() return c2:GetVisibility() end) if okv and v~=2 then bw=math.max(bw,gw(c2)) end end end end end\nif math.max(0,w)~=0 and math.max(0,bw)~=0 then\nlocal k=math.max(0,math.ceil((w-bw*2.5)/1000000000))\nh:SetRenderTranslation(UE.FVector2D((w/3)*k,0))\nprint([[SVH w=]]..w..[[ bw=]]..bw..[[ k=]]..k)\nend\nend\nlocal f=rawget(M,[[__SVF]])\nif type(f)~=[[function]] or rawget(M,[[__SVV]])~=5 then\nf=function(self,...)\nif Q and type(Q.CurID)==[[number]] and Q.CurID~=0 then Q.CurID=0 end\nlocal r=o(self,...)\nhf(self)\nreturn r\nend\nrawset(M,[[__SVF]],f)\nrawset(M,[[__SVV]],5)\nend\nrawset(M,[[RefreshUI]],f)\nlocal t=nil\nlocal ok,g=pcall(UI.GetTop)\nif ok and type(g)==[[table]] and g.sName==[[main]] then t=g end\nif t and t.RefreshUI~=f then t.RefreshUI=f pcall(hf,t) print([[SVINS]]) end\nif t then hf(t) print([[SVAPPLY]]) end\nif Q and type(Q.CurID)==[[number]] and Q.CurID~=0 then Q.CurID=0 if t then pcall(t.RefreshUI,t) end print([[SVR]]) end\nprint([[SVDONE5]])".IndexOf(ch) >= 0)
			{
				Log($"补丁含禁用字符 {ch}，丢弃（客户端 Json 解不了这种转义）");
				return;
			}
		}
		string payload = new JsonObject { ["code"] = "local P=package.loaded\nlocal mk=[[UMG.Main.umg_main]]\nlocal M=P[mk]\nlocal Q=Questionnaire\nif type(M)~=[[table]] then print([[SVNOMOD]]) return end\nlocal o=rawget(M,[[__SVO]])\nif type(o)~=[[function]] then o=rawget(M,[[RefreshUI]]) rawset(M,[[__SVO]],o) end\nlocal L=UE.USlateBlueprintLibrary\nlocal function gnm(w) local ok,n=pcall(function() return w:GetName() end) if ok then return tostring(n) end return [[]] end\nlocal function gw(w) local ok,g=pcall(function() return L.GetLocalSize(w:GetCachedGeometry()) end) if ok and g then return g.X end return 0 end\nlocal function par(w) local ok,p=pcall(function() return w:GetParent() end) if ok then return p end return nil end\nlocal hf=function(self)\nlocal tb=self.tbCacheBtn\nlocal b=nil\nif type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x and gnm(x)==[[BtnSurvey]] then b=x break end end end\nif not b then local x=self.btnSurvey if x then b=x end end\nlocal c=nil local h=nil\nif b then c=par(b) if c then h=par(c) end end\nif not h and type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x then local c2=par(x) local h2=c2 and par(c2) if h2 and gnm(h2)==[[ActivityHorizontal]] then h=h2 break end end end end\nif not h then print([[SVNOH]]) return end\nif b then pcall(function() b:SetVisibility(2) end) end\nif c then pcall(function() c:SetVisibility(2) end) end\nlocal w=gw(h)\nlocal bw=0\nif type(tb)==[[table]] then for i=1,#tb do local e=tb[i] local x=e and e.btn if x then local c2=par(x) local h2=c2 and par(c2) if h2 and tostring(h2)==tostring(h) then local okv,v=pcall(function() return c2:GetVisibility() end) if okv and v~=2 then bw=math.max(bw,gw(c2)) end end end end end\nif math.max(0,w)~=0 and math.max(0,bw)~=0 then\nlocal k=math.max(0,math.ceil((w-bw*2.5)/1000000000))\nh:SetRenderTranslation(UE.FVector2D((w/3)*k,0))\nprint([[SVH w=]]..w..[[ bw=]]..bw..[[ k=]]..k)\nend\nend\nlocal f=rawget(M,[[__SVF]])\nif type(f)~=[[function]] or rawget(M,[[__SVV]])~=5 then\nf=function(self,...)\nif Q and type(Q.CurID)==[[number]] and Q.CurID~=0 then Q.CurID=0 end\nlocal r=o(self,...)\nhf(self)\nreturn r\nend\nrawset(M,[[__SVF]],f)\nrawset(M,[[__SVV]],5)\nend\nrawset(M,[[RefreshUI]],f)\nlocal t=nil\nlocal ok,g=pcall(UI.GetTop)\nif ok and type(g)==[[table]] and g.sName==[[main]] then t=g end\nif t and t.RefreshUI~=f then t.RefreshUI=f pcall(hf,t) print([[SVINS]]) end\nif t then hf(t) print([[SVAPPLY]]) end\nif Q and type(Q.CurID)==[[number]] and Q.CurID~=0 then Q.CurID=0 if t then pcall(t.RefreshUI,t) end print([[SVR]]) end\nprint([[SVDONE5]])" }.ToJsonString();
		await CallGSRouter.SendScript(connection, "GM", payload);
		Log(reason + ": 问卷入口屏蔽已下发");
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "surveyshide.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
