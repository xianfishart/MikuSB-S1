using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.Packet.Recv.Login;

[Opcode(1011)]
public class HandlerNtfSetAttr : Handler
{
	public override async Task OnHandle(Connection connection, byte[] data, ushort seqNo)
	{
		NtfSetAttr req = NtfSetAttr.Parser.ParseFrom(data);
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == req.Gid && x.Sid == req.Sid);
			if (attr != null)
			{
				attr.Val = req.Val;
			}
			else
			{
				player.Data.Attrs.Add(new PlayerAttr
				{
					Gid = req.Gid,
					Sid = req.Sid,
					Val = req.Val
				});
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			await player.OnHeartBeat();
			await player.SendPacket(1011, new NtfSetAttr
			{
				Gid = req.Gid,
				Sid = req.Sid,
				Val = req.Val
			});
		}
	}
}
