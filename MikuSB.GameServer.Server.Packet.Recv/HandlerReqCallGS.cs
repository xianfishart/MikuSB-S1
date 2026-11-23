using System.Threading.Tasks;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv;

[Opcode(7)]
public class HandlerReqCallGS : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		ReqCallGS req = ReqCallGS.Parser.ParseFrom(data);
		await CallGSRouter.Route(connection, req, seqNo);
	}
}
