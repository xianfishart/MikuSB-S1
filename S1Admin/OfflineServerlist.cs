using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace S1Admin;

/// <summary>
/// 第 576 节：纯离线游玩的内置 serverlist 假服务。
///
/// 背景：客户端启动时必须先访问
///   https://snowbreak-ea-serverlist.amazingseasuncdn.com/query?version=..&amp;platform=PC
/// 拿到服务器列表才知道连哪台游戏服。断网时这一步必然失败，卡在「连接服务器」。
///
/// 做法：开关脚本（工具\离线开关.cmd）把该域名在 hosts 里指到 127.0.0.1，
/// 并写 Config\OfflineMode.json 的 enabled=true；本类在服务端启动时读该开关，
/// 若启用就在本机起 443（TLS）+ 80（明文）两个监听，任何路径都返回写死的
/// 服务器列表。客户端不校验证书，所以证书在运行时自签即可，不依赖外部文件。
///
/// 之所以放进服务端而不是外部脚本：服务端本身就是 .NET 10，SslStream 原生支持
/// TLS 1.3，不需要玩家机器上装 Python/openssl，开箱即用。
/// </summary>
public static class OfflineServerlist
{
	private const string HostName = "snowbreak-ea-serverlist.amazingseasuncdn.com";

	/// <summary>客户端只认这个格式：[{"platform":"PC","version":"3.6.0","host":..,"port":..}]</summary>
	private static readonly byte[] Body = Encoding.UTF8.GetBytes(
		"[{\"platform\":\"PC\",\"version\":\"3.6.0\",\"host\":\"43.153.191.68\",\"port\":5200}]");

	private static X509Certificate2? _cert;
	private static int _started;
	private static int _logged;

	public static bool Enabled()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "OfflineMode.json");
			if (!File.Exists(path))
			{
				return false;
			}
			if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject root
				&& root["enabled"] is JsonNode node)
			{
				return node.GetValueKind() == JsonValueKind.True;
			}
		}
		catch
		{
		}
		return false;
	}

	public static void StartIfEnabled()
	{
		if (!Enabled())
		{
			return;
		}
		if (Interlocked.Exchange(ref _started, 1) != 0)
		{
			return;
		}
		_cert = CreateCert();
		if (_cert == null)
		{
			Log("!! 自签证书创建失败，443 TLS 将不可用");
		}
		StartListener(443, tls: true);
		StartListener(80, tls: false);
		Log("offline serverlist ENABLED — hosts 需指向 127.0.0.1 " + HostName);
	}

	private static X509Certificate2? CreateCert()
	{
		try
		{
			using RSA rsa = RSA.Create(2048);
			CertificateRequest req = new CertificateRequest(
				"CN=" + HostName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
			san.AddDnsName(HostName);
			req.CertificateExtensions.Add(san.Build());
			req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
			req.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
			req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
			using X509Certificate2 tmp = req.CreateSelfSigned(
				DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
			return X509CertificateLoader.LoadPkcs12(tmp.Export(X509ContentType.Pfx), null,
				X509KeyStorageFlags.Exportable);
		}
		catch (Exception ex)
		{
			Log("cert err: " + ex.Message);
			return null;
		}
	}

	private static void StartListener(int port, bool tls)
	{
		Thread thread = new Thread(new ThreadStart(delegate
		{
			AcceptLoop(port, tls);
		}))
		{
			IsBackground = true,
			Name = "MikuOfflineSL" + port
		};
		thread.Start();
	}

	private static void AcceptLoop(int port, bool tls)
	{
		TcpListener listener = new TcpListener(IPAddress.Loopback, port);
		try
		{
			listener.Start();
		}
		catch (Exception ex)
		{
			Log("listen " + port + " failed: " + ex.Message);
			return;
		}
		Log("listening 127.0.0.1:" + port + (tls ? " tls" : " plain"));
		while (true)
		{
			TcpClient client;
			try
			{
				client = listener.AcceptTcpClient();
			}
			catch
			{
				continue;
			}
			TcpClient captured = client;
			ThreadPool.QueueUserWorkItem(delegate
			{
				Serve(captured, port, tls);
			});
		}
	}

	private static void Serve(TcpClient client, int port, bool tls)
	{
		try
		{
			client.ReceiveTimeout = 5000;
			client.SendTimeout = 5000;
			NetworkStream ns = client.GetStream();
			Stream stream = ns;
			if (tls)
			{
				if (_cert == null)
				{
					return;
				}
				SslStream ssl = new SslStream(ns, false);
				ssl.AuthenticateAsServer(new SslServerAuthenticationOptions
				{
					ServerCertificate = _cert,
					ClientCertificateRequired = false,
					EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
					CertificateRevocationCheckMode = X509RevocationMode.NoCheck
				});
				stream = ssl;
			}
			byte[] buf = new byte[4096];
			int n = 0;
			try
			{
				n = stream.Read(buf, 0, buf.Length);
			}
			catch
			{
			}
			string req = (n > 0) ? Encoding.ASCII.GetString(buf, 0, n) : "";
			string line = req.Length > 0 ? req.Split('\n')[0].Trim() : "(empty)";
			if (Interlocked.Exchange(ref _logged, 1) == 0 || req.Length > 0)
			{
				Log("REQ " + port + " " + line);
			}
			string head = "HTTP/1.1 200 OK\r\n"
				+ "Content-Type: application/json\r\n"
				+ "Content-Length: " + Body.Length + "\r\n"
				+ "Connection: close\r\n\r\n";
			byte[] hb = Encoding.ASCII.GetBytes(head);
			stream.Write(hb, 0, hb.Length);
			stream.Write(Body, 0, Body.Length);
			stream.Flush();
			try
			{
				stream.Dispose();
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Log("ERR " + port + " " + ex.Message);
		}
		finally
		{
			try
			{
				client.Close();
			}
			catch
			{
			}
		}
	}

	private static void Log(string msg)
	{
		try
		{
			string dir = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs");
			Directory.CreateDirectory(dir);
			File.AppendAllText(Path.Combine(dir, "offline-serverlist.log"),
				DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\r\n", Encoding.UTF8);
		}
		catch
		{
		}
	}
}
