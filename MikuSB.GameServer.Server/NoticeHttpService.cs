using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server;

public static class NoticeHttpService
{
	private const int Port = 8899;

	/// <summary>第 523 节：公告密文格式标记（announce_enc.py 生成）。</summary>
	private const string SealedFmt = "miku-ann-1";

	private static readonly byte[] SealKey = SHA256.HashData(Encoding.UTF8.GetBytes("MikuSB.S1.Announcement.2026::key"));

	private static readonly byte[] SealMacKey = SHA256.HashData(Encoding.UTF8.GetBytes("MikuSB.S1.Announcement.2026::mac"));

	private static string _announceState = "";

	private static bool _started;

	public static void Start()
	{
		if (!_started)
		{
			_started = true;
			Task.Run((Func<Task?>)AcceptLoop);
		}
	}

	private static async Task AcceptLoop()
	{
		try
		{
			TcpListener listener = new TcpListener(IPAddress.Loopback, 8899);
			listener.Start();
			while (true)
			{
				TcpClient client = await listener.AcceptTcpClientAsync();
				Task.Run(() => Handle(client));
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 第 523 节：公告加密加载。
	/// 文件必须是密文格式 {"fmt":"miku-ann-1","data":"..."}，解密成功才下发；
	/// 文件被篡改/替换成明文/损坏 → 一律回退到编译进服务端的内置默认公告，
	/// 保证被乱改时不会把改动内容下发给客户端。
	/// </summary>
	private static byte[] LoadAnnouncement()
	{
		string file = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Announcement.json");
		string state;
		byte[]? plain = null;
		if (!File.Exists(file))
		{
			state = "file-missing";
		}
		else
		{
			try
			{
				JsonNode? node = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8));
				if (node is JsonObject obj && (string?)obj["fmt"] == SealedFmt && (string?)obj["data"] is string data)
				{
					plain = Unseal(data);
					state = ((plain != null) ? "file-ok" : "file-tampered");
				}
				else
				{
					state = "file-plain-rejected";
				}
			}
			catch
			{
				state = "file-invalid";
			}
		}
		if (plain == null)
		{
			plain = Unseal(AnnouncementDefaults.Sealed);
			state = state + " -> fallback=" + ((plain != null) ? "ok" : "fail");
		}
		if (plain == null)
		{
			plain = Encoding.UTF8.GetBytes("{\"announce\":[]}");
		}
		if (state != _announceState)
		{
			_announceState = state;
			Log("announce " + state + " bytes=" + plain.Length);
		}
		return plain;
	}

	/// <summary>解密：base64(hmac16 || iv16 || AES-256-CBC(明文))，HMAC 校验不过返回 null。</summary>
	private static byte[]? Unseal(string sealedBase64)
	{
		try
		{
			byte[] raw = Convert.FromBase64String(sealedBase64);
			if (raw.Length < 48)
			{
				return null;
			}
			byte[] tag = raw[0..16];
			byte[] iv = raw[16..32];
			byte[] cipher = raw[32..];
			byte[] macInput = new byte[SealMacKey.Length + 16 + cipher.Length];
			Buffer.BlockCopy(SealMacKey, 0, macInput, 0, SealMacKey.Length);
			Buffer.BlockCopy(iv, 0, macInput, SealMacKey.Length, 16);
			Buffer.BlockCopy(cipher, 0, macInput, SealMacKey.Length + 16, cipher.Length);
			byte[] expect = SHA256.HashData(macInput)[0..16];
			if (!CryptographicOperations.FixedTimeEquals(tag, expect))
			{
				return null;
			}
			using Aes aes = Aes.Create();
			aes.Key = SealKey;
			return aes.DecryptCbc(cipher, iv, PaddingMode.PKCS7);
		}
		catch
		{
			return null;
		}
	}

	private static void Log(string message)
	{
		try
		{
			File.AppendAllText("D:\\DSH\\notice_http.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
		}
		catch
		{
		}
	}

	private static async Task Handle(TcpClient client)

	{
		try
		{
			using NetworkStream stream = client.GetStream();
			byte[] buffer = new byte[4096];
			int read = await stream.ReadAsync(buffer);
			string request = Encoding.ASCII.GetString(buffer, 0, read);
			string firstLine = request.Split('\n')[0];
			string path = firstLine.Split(' ').ElementAtOrDefault(1) ?? "";
			try
			{
				File.AppendAllText("D:\\DSH\\notice_http.log", $"{DateTime.Now:HH:mm:ss} {path}\n");
			}
			catch
			{
			}
			string status = "200 OK";
			byte[] body;
			if (path.Contains("announce/config", StringComparison.OrdinalIgnoreCase) || path.EndsWith("notice.json", StringComparison.OrdinalIgnoreCase))
			{
				body = LoadAnnouncement();
			}
			else
			{
				status = "404 Not Found";
				body = Encoding.UTF8.GetBytes("not found");
			}
			byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\n");
			await stream.WriteAsync(head);
			await stream.WriteAsync(body);
		}
		catch
		{
		}
		finally
		{
			client.Close();
		}
	}
}
