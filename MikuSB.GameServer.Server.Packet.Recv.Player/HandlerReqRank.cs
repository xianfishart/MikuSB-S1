using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Player;

[Opcode(53)]
public class HandlerReqRank : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		await connection.SendPacket(54, new RankInfo
		{
			Score = 0u,
			Rank = 0u,
			Sum = 0u,
			Info = string.Empty
		}, 0);
	}
}
