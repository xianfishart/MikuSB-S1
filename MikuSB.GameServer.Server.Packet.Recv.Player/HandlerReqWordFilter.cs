using System;
using System.IO;
using System.Threading.Tasks;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Recv.Player;

[Opcode(57)]
public class HandlerReqWordFilter : Handler
{
	private const string LogPath = "D:\\DSH\\.tools\\wordfilter.log";

	private const string ReplyPath = "D:\\DSH\\.tools\\wf_reply.hex";

	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		try
		{
			string dir = Path.GetDirectoryName("D:\\DSH\\.tools\\wordfilter.log");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.AppendAllText("D:\\DSH\\.tools\\wordfilter.log", $"{DateTime.Now:HH:mm:ss} uid={connection.Player?.Uid} len={data.Length} hex={Convert.ToHexString(data)}\n");
		}
		catch
		{
		}
		BasePacket packet = new BasePacket(58, LoadReply())
		{
			SeqNo = seqNo
		};
		await connection.SendPacket(packet, 0);
	}

	private static byte[] LoadReply()
	{
		try
		{
			if (File.Exists("D:\\DSH\\.tools\\wf_reply.hex"))
			{
				string text = File.ReadAllText("D:\\DSH\\.tools\\wf_reply.hex").Trim();
				if (text.Length > 0 && text.Length % 2 == 0)
				{
					return Convert.FromHexString(text);
				}
			}
		}
		catch
		{
		}
		return new byte[2] { 8, 127 };
	}
}
