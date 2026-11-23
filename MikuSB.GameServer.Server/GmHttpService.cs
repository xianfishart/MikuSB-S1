using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using S1Admin;

namespace MikuSB.GameServer.Server;

public static class GmHttpService
{
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
		ConsoleHost.Start();
		EraGacha.Start();
		// 体力每 5 分钟恢复 1 点，上限读 levels.json 的 MaxVigor（第 180 节）
		VigorRecoveryService.Start();
		Task.Run((Func<Task?>)AcceptLoop);
	}

	private static async Task AcceptLoop()
	{
		try
		{
			TcpListener listener = new TcpListener(IPAddress.Loopback, 1234);
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

	private static async Task Handle(TcpClient client)
	{
		try
		{
			using NetworkStream stream = client.GetStream();
			byte[] buffer = new byte[65536];
			int read = await stream.ReadAsync(buffer);
			string text = Encoding.UTF8.GetString(buffer, 0, read);
			int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
			string headText = ((split >= 0) ? text.Substring(0, split) : text);
			string body = ((split >= 0) ? text.Substring(split + 4) : "");
			Match lengthMatch = Regex.Match(headText, "Content-Length:\\s*(\\d+)", RegexOptions.IgnoreCase);
			int contentLength = (lengthMatch.Success ? int.Parse(lengthMatch.Groups[1].Value) : 0);
			while (Encoding.UTF8.GetByteCount(body) < contentLength)
			{
				int more = await stream.ReadAsync(buffer);
				if (more <= 0)
				{
					break;
				}
				body += Encoding.UTF8.GetString(buffer, 0, more);
			}
			string code = "";
			try
			{
				code = JsonNode.Parse(body)?["code"]?.ToString() ?? "";
			}
			catch
			{
			}
			Match match = Regex.Match(code, "GMOneKeyAddItem\\(\\s*(\\d+)\\s*\\)", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				int level = Math.Clamp(int.Parse(match.Groups[1].Value), 1, 80);
				foreach (Connection con in SocketListener.Connections.Values.OfType<Connection>())
				{
					if (con.Player != null)
					{
						con.Player.Data.Level = (uint)level;
						con.Player.Data.Exp = 0;
						DatabaseHelper.SaveDatabaseType(con.Player.Data);
						NtfSyncPlayer sync = new NtfSyncPlayer();
						sync.Core[0u] = con.Player.Data.Level;
						sync.Core[1u] = 0u;
						await con.SendPacket(1004, sync, 0);
					}
				}
			}
			byte[] response = Encoding.UTF8.GetBytes("{\"code\":0}");
			byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
			await stream.WriteAsync(head);
			await stream.WriteAsync(response);
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
