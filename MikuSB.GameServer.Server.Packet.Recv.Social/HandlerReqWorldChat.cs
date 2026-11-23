using System.Linq;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.TcpSharp;

namespace MikuSB.GameServer.Server.Packet.Recv.Social;

[Opcode(323)]
public class HandlerReqWorldChat : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		ChatMsg req;
		try
		{
			req = ChatMsg.Parser.ParseFrom(data);
		}
		catch
		{
			return;
		}
		ChatMsg msg = new ChatMsg
		{
			Type = ChatType.World,
			Sender = (ulong)player.Uid,
			Recver = 0uL,
			Emoji = req.Emoji,
			Text = ChatMessageHelper.NormalizeForClient(req.Text),
			Profile = player.Data.ToProfileProto(),
			TimeStamp = ChatMessageHelper.BuildClientTimestamp()
		};
		if (string.IsNullOrEmpty(msg.Text) && msg.Emoji == 0)
		{
			return;
		}
		foreach (Connection con in SocketListener.Connections.Values.OfType<Connection>())
		{
			if (con.Player != null && con != connection)
			{
				try
				{
					await con.Player.SendPacket(1041, msg);
				}
				catch
				{
				}
			}
		}
		await connection.SendPacket(324, msg, 0);
	}
}
