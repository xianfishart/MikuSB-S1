using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.Packet.Recv.Player;

[Opcode(321)]
public class HandlerReqChangeWorldChannel : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		await connection.SendPacket(322);
	}
}
