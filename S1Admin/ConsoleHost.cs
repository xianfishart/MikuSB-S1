using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.TcpSharp;

namespace S1Admin;

public static class ConsoleHost
{
	public const int Port = 21400;

	/// <summary>
	/// GM 调试口的 Bearer 口令：优先读 Config/AdminToken.txt，其次 Config/Config.json 的
	/// HttpServer.AdminToken。源码里不写死固定密钥。都读不到时用占位值。
	/// 独立文本文件优先，是因为 Config.json 会被启动流程按默认值重写，写在里面的口令会被冲掉。
	/// </summary>
	public static string Token
	{
		get
		{
			string fromFile = ReadTokenFile();
			if (fromFile.Length > 0)
			{
				return fromFile;
			}
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Config.json");
				if (File.Exists(path)
					&& JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject root
					&& root["HttpServer"] is JsonObject http
					&& http["AdminToken"] is JsonNode node)
				{
					string value = node.ToString().Trim();
					if (value.Length > 0)
					{
						return value;
					}
				}
			}
			catch
			{
			}
			return "dev-admin-token";
		}
	}

	/// <summary>读 Config/AdminToken.txt（一行口令）。没有或为空时返回空串。</summary>
	public static string ReadTokenFile()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "AdminToken.txt");
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path, Encoding.UTF8).Trim();
				if (text.Length > 0)
				{
					return text;
				}
			}
		}
		catch
		{
		}
		return "";
	}

	private static bool _started;

	private static readonly object Gate = new object();

	public static void Start()
	{
		lock (Gate)
		{
			if (_started)
			{
				return;
			}
			_started = true;
		}
		try
		{
			ConfigKeeper.Start();
		}
		catch
		{
		}
		try
		{
			Thread thread = new Thread(Loop);
			thread.IsBackground = true;
			thread.Name = "S1AdminConsole";
			thread.Start();
		}
		catch
		{
		}
	}

	private static void Loop()
	{
		TcpListener tcpListener = null;
		try
		{
			tcpListener = new TcpListener(IPAddress.Loopback, 21400);
			tcpListener.Start();
		}
		catch (Exception ex)
		{
			Log("listen failed: " + ex.Message);
			return;
		}
		Log("listening on 127.0.0.1:" + 21400);
		while (true)
		{
			TcpClient tcpClient = null;
			try
			{
				tcpClient = tcpListener.AcceptTcpClient();
			}
			catch
			{
				continue;
			}
			try
			{
				Handle(tcpClient);
			}
			catch
			{
			}
			finally
			{
				try
				{
					tcpClient.Close();
				}
				catch
				{
				}
			}
		}
	}

	private static void Log(string message)
	{
		try
		{
			string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
			Directory.CreateDirectory(text);
			File.AppendAllText(Path.Combine(text, "s1admin.log"), DateTime.Now.ToString("HH:mm:ss") + "\t" + message + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	private static void Handle(TcpClient client)
	{
		try
		{
			HandleCore(client);
		}
		catch (Exception ex)
		{
			Log("EX " + ex.GetType().Name + ": " + ex.Message + " | " + ex.StackTrace);
		}
	}

	private static void HandleCore(TcpClient client)
	{
		NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 15000;
		MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[65536];
		int num = -1;
		int num2 = 0;
		do
		{
			int num3 = stream.Read(array, 0, array.Length);
			if (num3 <= 0)
			{
				break;
			}
			memoryStream.Write(array, 0, num3);
			if (num < 0)
			{
				byte[] array2 = memoryStream.ToArray();
				num = IndexOfHeaderEnd(array2);
				if (num >= 0)
				{
					num2 = ParseContentLength(Encoding.UTF8.GetString(array2, 0, num));
				}
			}
		}
		while ((num < 0 || memoryStream.Length - (num + 4) < num2) && memoryStream.Length <= 8388608);
		byte[] array3 = memoryStream.ToArray();
		string head = ((num >= 0) ? Encoding.UTF8.GetString(array3, 0, num) : Encoding.UTF8.GetString(array3));
		string body = "";
		if (num >= 0 && array3.Length > num + 4)
		{
			body = Encoding.UTF8.GetString(array3, num + 4, array3.Length - num - 4);
		}
		string text = ParsePath(head);
		string method = ParseMethod(head);
		string query = "";
		int num4 = text.IndexOf('?');
		if (num4 >= 0)
		{
			query = text.Substring(num4 + 1);
			text = text.Substring(0, num4);
		}
		if (text.StartsWith("/api/") || text == "/api")
		{
			if (!IsAuthorized(head, query))
			{
				WriteResponse(stream, 401, "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"code\":401,\"message\":\"unauthorized\"}"));
				return;
			}
			AdminApi.Response response = AdminApi.Handle(method, text, query, body);
			Log(text + " -> " + response.Status + " " + response.Body.Length + "B");
			WriteResponse(stream, response.Status, response.ContentType, response.Body);
		}
		else if (text.StartsWith("/mailcdn/", StringComparison.OrdinalIgnoreCase))
		{
			string text3 = ServeMailCdn(stream, text.Substring(9));
			Log(text + " -> " + text3);
		}
		else
		{
			string text2 = ServeStatic(stream, text);
			Log(text + " -> " + text2);
		}
	}

	private static string _mailCdnRoot;

	private static string MailCdnRoot
	{
		get
		{
			if (_mailCdnRoot != null)
			{
				return _mailCdnRoot;
			}
			string[] candidates = new string[3]
			{
				Path.Combine(Directory.GetCurrentDirectory(), "MailContent"),
				"D:\\DSH\\mailpack\\src",
				Path.Combine(Directory.GetCurrentDirectory(), "Config", "MailContent")
			};
			foreach (string text in candidates)
			{
				if (Directory.Exists(text))
				{
					_mailCdnRoot = Path.GetFullPath(text);
					return _mailCdnRoot;
				}
			}
			_mailCdnRoot = Path.GetFullPath(candidates[0]);
			return _mailCdnRoot;
		}
	}

	/// <summary>本地邮件 CDN：/mailcdn/mail/audio/xxx.brief 等，替代官方 amazingseasuncdn</summary>
	private static string ServeMailCdn(NetworkStream stream, string rel)
	{
		string text = rel;
		int num = text.IndexOf('?');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		text = Uri.UnescapeDataString(text).TrimStart('/');
		if (text.Length == 0)
		{
			WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"));
			return "404";
		}
		string root = MailCdnRoot;
		string fullPath = Path.GetFullPath(Path.Combine(root, text.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
		{
			WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"));
			return "404";
		}
		string ext = Path.GetExtension(fullPath).ToLowerInvariant();
		string contentType;
		switch (ext)
		{
		case ".brief":
		case ".json":
		case ".txt":
			contentType = "application/json; charset=utf-8";
			break;
		case ".mp3":
			contentType = "audio/mpeg";
			break;
		case ".png":
			contentType = "image/png";
			break;
		case ".jpg":
		case ".jpeg":
			contentType = "image/jpeg";
			break;
		default:
			contentType = "application/octet-stream";
			break;
		}
		byte[] array;
		try
		{
			array = File.ReadAllBytes(fullPath);
		}
		catch (IOException)
		{
			WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("read error"));
			return "404";
		}
		WriteResponse(stream, 200, contentType, array);
		return "200 " + array.Length + "B";
	}

	private static int IndexOfHeaderEnd(byte[] data)
	{
		for (int i = 0; i + 3 < data.Length; i++)
		{
			if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
			{
				return i;
			}
		}
		return -1;
	}

	private static int ParseContentLength(string head)
	{
		string[] array = head.Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length > 15 && text.Substring(0, 15).ToLowerInvariant() == "content-length:" && int.TryParse(text.Substring(15).Trim(), out var result))
			{
				return result;
			}
		}
		return 0;
	}

	private static string ParsePath(string head)
	{
		int num = head.IndexOf(' ');
		if (num < 0)
		{
			return "/";
		}
		int num2 = head.IndexOf(' ', num + 1);
		if (num2 >= 0)
		{
			return head.Substring(num + 1, num2 - num - 1);
		}
		return head.Substring(num + 1);
	}

	private static void WriteResponse(NetworkStream stream, int status, string contentType, byte[] payload)
	{
		if (1 == 0)
		{
		}
		string text = status switch
		{
			401 => "401 Unauthorized", 
			404 => "404 Not Found", 
			_ => "200 OK", 
		};
		if (1 == 0)
		{
		}
		string text2 = text;
		string s = "HTTP/1.1 " + text2 + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + payload.Length + "\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
		byte[] bytes = Encoding.ASCII.GetBytes(s);
		stream.Write(bytes, 0, bytes.Length);
		stream.Write(payload, 0, payload.Length);
		stream.Flush();
	}

	private static string ParseMethod(string head)
	{
		int num = head.IndexOf(' ');
		return (num > 0) ? head.Substring(0, num).ToUpperInvariant() : "GET";
	}

	private static bool IsAuthorized(string head, string query)
	{
		string[] array = head.Split('\n');
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (text2.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text2.Substring("Authorization:".Length).Trim();
				if (text3.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
				{
					text3 = text3.Substring(7).Trim();
				}
				if (text3 == Token)
				{
					return true;
				}
			}
		}
		string[] array2 = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text4 in array2)
		{
			int num = text4.IndexOf('=');
			if (num > 0 && string.Equals(text4.Substring(0, num), "token", StringComparison.OrdinalIgnoreCase) && Uri.UnescapeDataString(text4.Substring(num + 1)) == Token)
			{
				return true;
			}
		}
		return false;
	}

	private static string ServeStatic(NetworkStream stream, string path)
	{
		string text = Path.Combine(Directory.GetCurrentDirectory(), "Web");
		string text2 = path.TrimStart('/');
		if (text2.Length == 0)
		{
			text2 = "index.html";
		}
		string fullPath = Path.GetFullPath(Path.Combine(text, text2.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(Path.GetFullPath(text), StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
		{
			byte[] bytes = Encoding.UTF8.GetBytes("not found");
			WriteResponse(stream, 404, "text/plain; charset=utf-8", bytes);
			return "404";
		}
		string text3 = Path.GetExtension(fullPath).ToLowerInvariant();
		if (1 == 0)
		{
		}
		string text4;
		switch (text3)
		{
		case ".html":
		case ".htm":
			text4 = "text/html; charset=utf-8";
			break;
		case ".js":
			text4 = "application/javascript; charset=utf-8";
			break;
		case ".css":
			text4 = "text/css; charset=utf-8";
			break;
		case ".json":
			text4 = "application/json; charset=utf-8";
			break;
		case ".png":
			text4 = "image/png";
			break;
		case ".jpg":
		case ".jpeg":
			text4 = "image/jpeg";
			break;
		case ".svg":
			text4 = "image/svg+xml";
			break;
		case ".ico":
			text4 = "image/x-icon";
			break;
		default:
			text4 = "application/octet-stream";
			break;
		}
		if (1 == 0)
		{
		}
		string text5 = text4;
		byte[] array = File.ReadAllBytes(fullPath);
		WriteResponse(stream, 200, text5, array);
		return text5 + " " + array.Length + "B";
	}

	public static AdminApi.Response ExecuteLua(string raw)
	{
		string text = (raw ?? "").Trim();
		if (text.StartsWith("/"))
		{
			text = text.Substring(1);
		}
		if (!text.StartsWith("lua"))
		{
			return AdminApi.Response.Json("{\"code\":2,\"message\":\"only /lua is supported\"}");
		}
		text = text.Substring(3).Trim();
		int num = 0;
		int num2 = text.LastIndexOf(" @");
		if (num2 > 0 && int.TryParse(text.Substring(num2 + 2).Trim(), out var result))
		{
			num = result;
			text = text.Substring(0, num2).Trim();
		}
		Connection connection = null;
		if (num > 0)
		{
			connection = Listener.GetActiveConnection(num);
			if (connection == null)
			{
				foreach (KeyValuePair<long, SocketConnection> connection4 in SocketListener.Connections)
				{
					if (connection4.Value is Connection { Player: not null } connection2 && connection2.Player.Uid == num)
					{
						connection = connection2;
						break;
					}
				}
			}
		}
		else
		{
			foreach (KeyValuePair<long, SocketConnection> connection5 in SocketListener.Connections)
			{
				if (connection5.Value is Connection { Player: not null } connection3)
				{
					connection = connection3;
					break;
				}
			}
		}
		if (connection == null)
		{
			return AdminApi.Response.Json(new JsonObject
			{
				["code"] = 3,
				["message"] = ((num > 0) ? ("player " + num + " is not online") : "no active player")
			}.ToJsonString());
		}
		JsonObject jsonObject = new JsonObject();
		jsonObject["code"] = text;
		try
		{
			CallGSRouter.SendScript(connection, "GM", jsonObject.ToJsonString()).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			return AdminApi.Response.Json(new JsonObject
			{
				["code"] = 4,
				["message"] = ex.Message
			}.ToJsonString());
		}
		return AdminApi.Response.Json(new JsonObject
		{
			["code"] = 0,
			["messages"] = new JsonArray(),
			["uid"] = connection.Player.Uid,
			["length"] = text.Length
		}.ToJsonString());
	}
}
