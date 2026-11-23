using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(17)]
public class HandlerReqSetNewGuide : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		await connection.SendPacket(18);
	}
}
