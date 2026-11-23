using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS;

public static class CallGSRouter
{
	private static readonly Logger Logger = new Logger("CallGS");

	private static readonly Dictionary<string, ICallGSHandler> Handlers = new Dictionary<string, ICallGSHandler>();

	private const string UnavailableTipKey = "ui.TxtNotOpen";

	/// <summary>抓包开关文件（存在即开启）。</summary>
	private static readonly string TraceFlagPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "callgs_trace.on");

	private static bool TraceOn
	{
		get
		{
			try
			{
				return File.Exists(TraceFlagPath);
			}
			catch
			{
				return false;
			}
		}
	}

	public static bool TryGetHandler(string api, out ICallGSHandler handler)
	{
		if (string.IsNullOrWhiteSpace(api))
		{
			handler = null;
			return false;
		}
		return Handlers.TryGetValue(api, out handler);
	}

	public static void Init()
	{
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types)
		{
			CallGSApiAttribute customAttribute = type.GetCustomAttribute<CallGSApiAttribute>();
			if (customAttribute != null)
			{
				Handlers[customAttribute.Api] = (ICallGSHandler)Activator.CreateInstance(type);
			}
		}
		Logger.Info($"Registered {Handlers.Count} CallGS handlers.");
	}

	public static async Task Route(Connection connection, ReqCallGS req, ushort seqNo)
	{
		// 第 350 节：CallGS 参数抓包开关 —— 只要 Config/callgs_trace.on 存在就把每次
		// 请求的 api + 原始 JSON 参数写进 Server.log，用来查客户端到底发了什么。
		// 用文件当开关，抓完删掉就静音，不必重新编译。
		if (TraceOn)
		{
			string raw = req.Param ?? string.Empty;
			if (raw.Length > 1200)
			{
				raw = raw.Substring(0, 1200) + "...(len=" + (req.Param?.Length ?? 0) + ")";
			}
			Logger.Info("[TRACE] " + req.Api + " " + raw);
		}
		if (Handlers.TryGetValue(req.Api, out ICallGSHandler handler))
		{
			try
			{
				await handler.Handle(connection, req.Param, seqNo);
				if (connection.Player != null)
				{
					await connection.Player.OnHeartBeat();
				}
			}
			catch (Exception ex)
			{
				Logger.Error("[" + req.Api + "] " + ex.Message, ex);
				await SendUnavailableResponse(connection, req.Api);
			}
		}
		else
		{
			Logger.Error("No handler for CallGS API: " + req.Api);
			await SendUnavailableResponse(connection, req.Api);
		}
	}

	public static async Task SendScript(Connection connection, string api, string arg, NtfSyncPlayer extra = null)
	{
		// 第 425 节：Api 为空时客户端 S2C.lua:32 会抛 "No command named: "。
		// 这里把调用栈记下来，方便定位是哪个功能发的空名包（定位完再决定改成不发还是换名字）。
		if (string.IsNullOrEmpty(api))
		{
			string trace = new System.Diagnostics.StackTrace(1, true).ToString().Replace("\r", "").Replace("\n", " | ");
			if (trace.Length > 700)
			{
				trace = trace.Substring(0, 700);
			}
			Logger.Info("[EMPTYAPI] " + trace);
		}
		NtfCallScript rsp = new NtfCallScript
		{
			Api = api,
			Arg = arg,
			ExtraSync = extra
		};
		await connection.SendPacket(1010, rsp, 0);
	}

	private static Task SendUnavailableResponse(Connection connection, string api)
	{
		return SendScript(connection, api, "{\"sErr\":\"ui.TxtNotOpen\",\"sError\":\"ui.TxtNotOpen\"}");
	}
}
