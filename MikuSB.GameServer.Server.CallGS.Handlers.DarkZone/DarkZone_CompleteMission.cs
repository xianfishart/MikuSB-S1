using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("DarkZone_CompleteMission")]
public class DarkZone_CompleteMission : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		try
		{
			int id = DarkZoneData.ReadInt(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nId"]);
			if (id > 0)
			{
				uint sid = (uint)(id / 30);
				int bit = id % 30;
				PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 191 && x.Sid == sid);
				if (attr == null)
				{
					attr = new PlayerAttr
					{
						Gid = 191u,
						Sid = sid,
						Val = 0u
					};
					player.Data.Attrs.Add(attr);
				}
				attr.Val |= (uint)(1 << bit);
				sync.Custom[player.ToPackedAttrKey(191u, sid)] = attr.Val;
				sync.Custom[player.ToShiftedAttrKey(191u, sid)] = attr.Val;
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
		await CallGSRouter.SendScript(connection, "DarkZone_CompleteMission", "{}", sync);
	}
}
