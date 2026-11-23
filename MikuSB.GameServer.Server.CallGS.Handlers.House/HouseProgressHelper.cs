using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseProgressHelper
{
	public const uint Gid = 101u;

	public static async Task SetAttr(Connection connection, uint sid, uint value)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(attr);
		}
		else
		{
			attr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = attr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"CharacterAttrChanged\"}", sync);
	}
}
