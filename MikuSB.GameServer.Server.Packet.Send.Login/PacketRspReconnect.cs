using Google.Protobuf;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.TcpSharp;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Server.Packet.Send.Login;

public class PacketRspReconnect : BasePacket
{
	public PacketRspReconnect(PlayerInstance player, string sessionId = "")
		: base(4)
	{
		RspReconnect message = new RspReconnect
		{
			Timestamp = (uint)Extensions.GetUnixSec(),
			WorldChannel = 1u,
			SessionId = (sessionId ?? ""),
			NeedRename = false,
			Data = player.ToPlayerProto()
		};
		SetData(message.ToByteArray());
	}
}
