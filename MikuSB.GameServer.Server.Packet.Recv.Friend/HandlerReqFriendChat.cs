using System.Threading.Tasks;
using Google.Protobuf;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.Packet.Recv.Friend;

[Opcode(325)]
public class HandlerReqFriendChat : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		ChatMsg req = ChatMsg.Parser.ParseFrom(data);
		string json = JsonFormatter.Default.Format(req);
		Logger.GetByClassName().Debug(json ?? "");
		await connection.Player.ReceiveMessage((uint)connection.Player.Uid, (uint)req.Recver, req.Text, (uint)req.Emoji);
		await connection.SendPacket(326);
	}
}
