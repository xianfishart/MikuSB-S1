using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[CallGSApi("House_Request")]
public class House_Request : ICallGSHandler
{
	private static readonly Dictionary<string, IHouseFuncHandler> Handlers;

	/// <summary>请求流水落盘路径（修复记录第 272 节排查用，超 4MB 自动清空）。</summary>
	private const string RequestLogPath = "D:/DSH/.tools/house_req.log";

	private const long RequestLogMaxBytes = 4194304L;

	private static void LogRequest(string funcName, string param)
	{
		try
		{
			if (File.Exists(RequestLogPath))
			{
				FileInfo fileInfo = new FileInfo(RequestLogPath);
				if (fileInfo.Length > RequestLogMaxBytes)
				{
					File.WriteAllText(RequestLogPath, string.Empty);
				}
			}
			string text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + funcName + " " + (param.Length > 800 ? param.Substring(0, 800) : param) + Environment.NewLine;
			File.AppendAllText(RequestLogPath, text);
		}
		catch
		{
		}
	}

	static House_Request()
	{
		Handlers = new Dictionary<string, IHouseFuncHandler>();
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types)
		{
			foreach (HouseFuncAttribute customAttribute in type.GetCustomAttributes<HouseFuncAttribute>())
			{
				Handlers[customAttribute.FuncName] = (IHouseFuncHandler)Activator.CreateInstance(type);
			}
		}
	}

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		HouseRequestParam req = JsonSerializer.Deserialize<HouseRequestParam>(param);
		if (req?.FuncName == null)
		{
			return;
		}
		// 2026-11-13 追加（修复记录第 272 节）：把每个 House_Request 的 FuncName 与原始报文落盘，
		// 便于定位「客户端到底发了哪个函数名 / 有没有发」这类问题（如神经调理五星奖励点了没反应）。
		LogRequest(req.FuncName, param);
		if (Handlers.TryGetValue(req.FuncName, out IHouseFuncHandler handler))
		{
			await handler.Handle(connection, param);
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root));
		}
	}
}
