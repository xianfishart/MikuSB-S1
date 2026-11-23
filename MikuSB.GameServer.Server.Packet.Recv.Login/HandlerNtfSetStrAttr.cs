using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(1012)]
public class HandlerNtfSetStrAttr : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		NtfSetStrAttr req = NtfSetStrAttr.Parser.ParseFrom(data);
		PlayerInstance player = connection.Player;
		player.SetStrAttr(req.Gid, req.Sid, req.Val);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await player.OnHeartBeat();
	}
}
