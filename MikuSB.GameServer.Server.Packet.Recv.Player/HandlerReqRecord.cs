using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using MikuSB.GameServer.Game.Gacha;
using MikuSB.GameServer.Game.Player;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Recv.Player;

[Opcode(25)]
public class HandlerReqRecord : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			List<string> records = (from x in GachaRecordStore.Get(key: ParseFirstString(data) ?? "", uid: player.Uid)
				select JsonSerializer.Serialize(new
				{
					tbGDPL = x.Gdpl,
					nTime = x.Time,
					nOrder = x.Order
				})).ToList();
			await connection.SendPacket(new BasePacket(26, BuildResponse(records)), 0);
		}
	}

	private static byte[] BuildResponse(List<string> records)
	{
		using MemoryStream memoryStream = new MemoryStream();
		CodedOutputStream codedOutputStream = new CodedOutputStream(memoryStream);
		foreach (string record in records)
		{
			codedOutputStream.WriteTag(1, WireFormat.WireType.LengthDelimited);
			codedOutputStream.WriteString(record);
		}
		codedOutputStream.Flush();
		return memoryStream.ToArray();
	}

	private static string? ParseFirstString(byte[] data)
	{
		if (data.Length == 0)
		{
			return null;
		}
		try
		{
			CodedInputStream codedInputStream = new CodedInputStream(data);
			while (!codedInputStream.IsAtEnd)
			{
				uint num = codedInputStream.ReadTag();
				if (num == 0)
				{
					break;
				}
				if (WireFormat.GetTagFieldNumber(num) == 1 && WireFormat.GetTagWireType(num) == WireFormat.WireType.LengthDelimited)
				{
					return codedInputStream.ReadString();
				}
				codedInputStream.SkipLastField();
			}
		}
		catch
		{
		}
		try
		{
			return Encoding.UTF8.GetString(data).Trim(new char[5] { '\0', ' ', '\r', '\n', '\t' });
		}
		catch
		{
			return null;
		}
	}
}
