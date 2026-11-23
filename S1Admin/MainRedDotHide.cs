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

public static class MainRedDotHide
{
	private static readonly char[] ForbiddenChars = new char[6] { '+', '<', '>', '&', '\'', '"' };

	private static readonly int[] Delays = new int[11]
	{
		3000, 8000, 15000, 25000, 40000, 60000, 90000, 120000, 180000, 240000,
		300000
	};

	private static readonly object LogGate = new object();

	public const string Lua = "local P=package.loaded\nlocal mk=[[UMG.Main.umg_main]]\nlocal M=P[mk]\nif type(M)~=[[table]] then print([[RDNOMOD]]) return end\nlocal WU=WidgetUtils\nlocal K1=[[MissionReward]]\nlocal K2=[[MissionNew]]\nlocal function hit(s)\nif s==K1 or s==K2 then return true end\nlocal p=string.sub(s,1,3)\nif p==[[Btn]] or p==[[Txt]] then return false end\nlocal c=string.gsub(s,[[_[0-9][0-9]*$]],[[ ]])\nif string.sub(c,1,3)==[[New]] then return true end\nif string.sub(c,-3)==[[New]] then return true end\nif string.sub(c,1,3)==[[Dot]] then return true end\nif string.sub(c,-3)==[[Red]] then return true end\nreturn false\nend\nlocal function cn(w) local ok,n=pcall(function() return w:GetName() end) if ok then return tostring(n) end return [[]] end\nlocal function hide(w) pcall(function() w:SetVisibility(1) end) end\nlocal function apply(w,seen)\nif w==nil then return end\nlocal key=tostring(w)\nif seen[key]==1 then return end\nseen[key]=1\nlocal s=cn(w)\nif s~=[[]] and hit(s) then hide(w) end\nlocal okw,wt=pcall(function() return w.WidgetTree end)\nif okw and wt~=nil then\nlocal okr,r=pcall(function() return wt.RootWidget end)\nif okr and r~=nil then apply(r,seen) end\nend\nlocal ok,n=pcall(function() return w:GetChildrenCount() end)\nif not ok then return end\nlocal i=0\nwhile i~=n do\nlocal ok2,c=pcall(function() return w:GetChildAt(i) end)\nif ok2 and c~=nil then apply(c,seen) end\ni=i-(-1)\nend\nend\nlocal function sweep()\nlocal n=0\nlocal seen={}\nfor k,w in pairs(UI.tbWidget) do\nif type(w)==[[table]] and w.Object~=nil then\nlocal ok,r=pcall(function() return w.Object.WidgetTree.RootWidget end)\nif ok and r~=nil then apply(r,seen) n=n-(-1) end\nend\nend\nreturn n\nend\nlocal function hookset(key,tag)\nlocal o=rawget(WU,key)\nif type(o)~=[[function]] then return end\nif rawget(WU,tag)==1 then return end\nrawset(WU,tag,1)\nrawset(WU,key,function(w,b)\nlocal s=cn(w)\nif s~=[[]] and hit(s) then return o(w,false) end\nreturn o(w,b)\nend)\nend\nlocal function hookone(key,tag)\nlocal o=rawget(WU,key)\nif type(o)~=[[function]] then return end\nif rawget(WU,tag)==1 then return end\nrawset(WU,tag,1)\nrawset(WU,key,function(w)\nlocal s=cn(w)\nif s~=[[]] and hit(s) then hide(w) return end\nreturn o(w)\nend)\nend\nhookset([[SetVisibleOrCollapsed]],[[__RDS]])\nhookset([[SetCollapsedOrSelfHitTestInvisible]],[[__RD2]])\nhookset([[SetHitTestInvisibleOrCollapsed]],[[__RD3]])\nhookset([[SetSelfHitTestInvisibleOrHidden]],[[__RD4]])\nhookone([[Visible]],[[__RD5]])\nhookone([[Hidden]],[[__RD6]])\nhookone([[HitTestInvisible]],[[__RD7]])\nhookone([[SelfHitTestInvisible]],[[__RD8]])\nlocal o=rawget(M,[[__RDO]])\nif type(o)~=[[function]] then o=rawget(M,[[RefreshUI]]) rawset(M,[[__RDO]],o) end\nlocal f=rawget(M,[[__RDF]])\nif type(f)~=[[function]] then\nf=function(self,...)\nlocal r=o(self,...)\npcall(sweep)\nreturn r\nend\nrawset(M,[[__RDF]],f)\nend\nrawset(M,[[RefreshUI]],f)\nlocal t=nil\nlocal ok,g=pcall(UI.GetTop)\nif ok and type(g)==[[table]] and g.sName==[[main]] then t=g end\nif t and t.RefreshUI~=f then t.RefreshUI=f end\nlocal n=sweep()\nprint([[RDDONE]],n)";

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
			if ("local P=package.loaded\nlocal mk=[[UMG.Main.umg_main]]\nlocal M=P[mk]\nif type(M)~=[[table]] then print([[RDNOMOD]]) return end\nlocal WU=WidgetUtils\nlocal K1=[[MissionReward]]\nlocal K2=[[MissionNew]]\nlocal function hit(s)\nif s==K1 or s==K2 then return true end\nlocal p=string.sub(s,1,3)\nif p==[[Btn]] or p==[[Txt]] then return false end\nlocal c=string.gsub(s,[[_[0-9][0-9]*$]],[[ ]])\nif string.sub(c,1,3)==[[New]] then return true end\nif string.sub(c,-3)==[[New]] then return true end\nif string.sub(c,1,3)==[[Dot]] then return true end\nif string.sub(c,-3)==[[Red]] then return true end\nreturn false\nend\nlocal function cn(w) local ok,n=pcall(function() return w:GetName() end) if ok then return tostring(n) end return [[]] end\nlocal function hide(w) pcall(function() w:SetVisibility(1) end) end\nlocal function apply(w,seen)\nif w==nil then return end\nlocal key=tostring(w)\nif seen[key]==1 then return end\nseen[key]=1\nlocal s=cn(w)\nif s~=[[]] and hit(s) then hide(w) end\nlocal okw,wt=pcall(function() return w.WidgetTree end)\nif okw and wt~=nil then\nlocal okr,r=pcall(function() return wt.RootWidget end)\nif okr and r~=nil then apply(r,seen) end\nend\nlocal ok,n=pcall(function() return w:GetChildrenCount() end)\nif not ok then return end\nlocal i=0\nwhile i~=n do\nlocal ok2,c=pcall(function() return w:GetChildAt(i) end)\nif ok2 and c~=nil then apply(c,seen) end\ni=i-(-1)\nend\nend\nlocal function sweep()\nlocal n=0\nlocal seen={}\nfor k,w in pairs(UI.tbWidget) do\nif type(w)==[[table]] and w.Object~=nil then\nlocal ok,r=pcall(function() return w.Object.WidgetTree.RootWidget end)\nif ok and r~=nil then apply(r,seen) n=n-(-1) end\nend\nend\nreturn n\nend\nlocal function hookset(key,tag)\nlocal o=rawget(WU,key)\nif type(o)~=[[function]] then return end\nif rawget(WU,tag)==1 then return end\nrawset(WU,tag,1)\nrawset(WU,key,function(w,b)\nlocal s=cn(w)\nif s~=[[]] and hit(s) then return o(w,false) end\nreturn o(w,b)\nend)\nend\nlocal function hookone(key,tag)\nlocal o=rawget(WU,key)\nif type(o)~=[[function]] then return end\nif rawget(WU,tag)==1 then return end\nrawset(WU,tag,1)\nrawset(WU,key,function(w)\nlocal s=cn(w)\nif s~=[[]] and hit(s) then hide(w) return end\nreturn o(w)\nend)\nend\nhookset([[SetVisibleOrCollapsed]],[[__RDS]])\nhookset([[SetCollapsedOrSelfHitTestInvisible]],[[__RD2]])\nhookset([[SetHitTestInvisibleOrCollapsed]],[[__RD3]])\nhookset([[SetSelfHitTestInvisibleOrHidden]],[[__RD4]])\nhookone([[Visible]],[[__RD5]])\nhookone([[Hidden]],[[__RD6]])\nhookone([[HitTestInvisible]],[[__RD7]])\nhookone([[SelfHitTestInvisible]],[[__RD8]])\nlocal o=rawget(M,[[__RDO]])\nif type(o)~=[[function]] then o=rawget(M,[[RefreshUI]]) rawset(M,[[__RDO]],o) end\nlocal f=rawget(M,[[__RDF]])\nif type(f)~=[[function]] then\nf=function(self,...)\nlocal r=o(self,...)\npcall(sweep)\nreturn r\nend\nrawset(M,[[__RDF]],f)\nend\nrawset(M,[[RefreshUI]],f)\nlocal t=nil\nlocal ok,g=pcall(UI.GetTop)\nif ok and type(g)==[[table]] and g.sName==[[main]] then t=g end\nif t and t.RefreshUI~=f then t.RefreshUI=f end\nlocal n=sweep()\nprint([[RDDONE]],n)".IndexOf(ch) >= 0)
			{
				Log($"补丁含禁用字符 {ch}，丢弃（客户端 Json 解不了这种转义）");
				return;
			}
		}
		string payload = new JsonObject { ["code"] = "local P=package.loaded\nlocal mk=[[UMG.Main.umg_main]]\nlocal M=P[mk]\nif type(M)~=[[table]] then print([[RDNOMOD]]) return end\nlocal WU=WidgetUtils\nlocal K1=[[MissionReward]]\nlocal K2=[[MissionNew]]\nlocal function hit(s)\nif s==K1 or s==K2 then return true end\nlocal p=string.sub(s,1,3)\nif p==[[Btn]] or p==[[Txt]] then return false end\nlocal c=string.gsub(s,[[_[0-9][0-9]*$]],[[ ]])\nif string.sub(c,1,3)==[[New]] then return true end\nif string.sub(c,-3)==[[New]] then return true end\nif string.sub(c,1,3)==[[Dot]] then return true end\nif string.sub(c,-3)==[[Red]] then return true end\nreturn false\nend\nlocal function cn(w) local ok,n=pcall(function() return w:GetName() end) if ok then return tostring(n) end return [[]] end\nlocal function hide(w) pcall(function() w:SetVisibility(1) end) end\nlocal function apply(w,seen)\nif w==nil then return end\nlocal key=tostring(w)\nif seen[key]==1 then return end\nseen[key]=1\nlocal s=cn(w)\nif s~=[[]] and hit(s) then hide(w) end\nlocal okw,wt=pcall(function() return w.WidgetTree end)\nif okw and wt~=nil then\nlocal okr,r=pcall(function() return wt.RootWidget end)\nif okr and r~=nil then apply(r,seen) end\nend\nlocal ok,n=pcall(function() return w:GetChildrenCount() end)\nif not ok then return end\nlocal i=0\nwhile i~=n do\nlocal ok2,c=pcall(function() return w:GetChildAt(i) end)\nif ok2 and c~=nil then apply(c,seen) end\ni=i-(-1)\nend\nend\nlocal function sweep()\nlocal n=0\nlocal seen={}\nfor k,w in pairs(UI.tbWidget) do\nif type(w)==[[table]] and w.Object~=nil then\nlocal ok,r=pcall(function() return w.Object.WidgetTree.RootWidget end)\nif ok and r~=nil then apply(r,seen) n=n-(-1) end\nend\nend\nreturn n\nend\nlocal function hookset(key,tag)\nlocal o=rawget(WU,key)\nif type(o)~=[[function]] then return end\nif rawget(WU,tag)==1 then return end\nrawset(WU,tag,1)\nrawset(WU,key,function(w,b)\nlocal s=cn(w)\nif s~=[[]] and hit(s) then return o(w,false) end\nreturn o(w,b)\nend)\nend\nlocal function hookone(key,tag)\nlocal o=rawget(WU,key)\nif type(o)~=[[function]] then return end\nif rawget(WU,tag)==1 then return end\nrawset(WU,tag,1)\nrawset(WU,key,function(w)\nlocal s=cn(w)\nif s~=[[]] and hit(s) then hide(w) return end\nreturn o(w)\nend)\nend\nhookset([[SetVisibleOrCollapsed]],[[__RDS]])\nhookset([[SetCollapsedOrSelfHitTestInvisible]],[[__RD2]])\nhookset([[SetHitTestInvisibleOrCollapsed]],[[__RD3]])\nhookset([[SetSelfHitTestInvisibleOrHidden]],[[__RD4]])\nhookone([[Visible]],[[__RD5]])\nhookone([[Hidden]],[[__RD6]])\nhookone([[HitTestInvisible]],[[__RD7]])\nhookone([[SelfHitTestInvisible]],[[__RD8]])\nlocal o=rawget(M,[[__RDO]])\nif type(o)~=[[function]] then o=rawget(M,[[RefreshUI]]) rawset(M,[[__RDO]],o) end\nlocal f=rawget(M,[[__RDF]])\nif type(f)~=[[function]] then\nf=function(self,...)\nlocal r=o(self,...)\npcall(sweep)\nreturn r\nend\nrawset(M,[[__RDF]],f)\nend\nrawset(M,[[RefreshUI]],f)\nlocal t=nil\nlocal ok,g=pcall(UI.GetTop)\nif ok and type(g)==[[table]] and g.sName==[[main]] then t=g end\nif t and t.RefreshUI~=f then t.RefreshUI=f end\nlocal n=sweep()\nprint([[RDDONE]],n)" }.ToJsonString();
		await CallGSRouter.SendScript(connection, "GM", payload);
		Log(reason + ": 黄点隐藏已下发");
	}

	private static void Log(string message)
	{
		try
		{
			lock (LogGate)
			{
				string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
				Directory.CreateDirectory(text);
				File.AppendAllText(Path.Combine(text, "reddothide.log"), $"{DateTime.Now:HH:mm:ss}\t{message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
