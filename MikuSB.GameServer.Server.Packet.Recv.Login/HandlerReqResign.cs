using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(23)]
public class HandlerReqResign : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			string signature = ParseString(data);
			if (!string.IsNullOrWhiteSpace(signature))
			{
				player.Data.Signature = signature.Trim();
				DatabaseHelper.UpdateInstance(player.Data);
			}
			await player.OnHeartBeat();
		}
		await connection.SendPacket(24, new StringValue
		{
			Value = (player?.Data.Signature ?? string.Empty)
		}, 0);
	}

	private static string? ParseString(byte[] data)
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
				if (WireFormat.GetTagWireType(num) == WireFormat.WireType.LengthDelimited)
				{
					string text = codedInputStream.ReadString();
					if (!string.IsNullOrWhiteSpace(text))
					{
						return text;
					}
				}
				else
				{
					codedInputStream.SkipLastField();
				}
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
