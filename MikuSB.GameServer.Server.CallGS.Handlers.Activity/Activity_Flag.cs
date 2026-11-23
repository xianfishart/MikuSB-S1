using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_Flag")]
public class Activity_Flag : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int activityId = 0;
		int type = 0;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(param);
			if (doc.RootElement.TryGetProperty("nId", out var nIdNode))
			{
				activityId = nIdNode.GetInt32();
			}
			if (doc.RootElement.TryGetProperty("nType", out var typeNode))
			{
				type = typeNode.GetInt32();
			}
		}
		catch
		{
		}
		if (activityId > 0)
		{
			uint sid = (uint)(activityId * 10 + 1);
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 50 && x.Sid == sid);
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = 50u,
					Sid = sid,
					Val = 0u
				};
				player.Data.Attrs.Add(attr);
			}
			attr.Val |= (uint)((type != 1) ? 1 : 2);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.Custom[player.ToPackedAttrKey(50u, sid)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(50u, sid)] = attr.Val;
			DatabaseHelper.SaveDatabaseType(player.Data);
			await CallGSRouter.SendScript(connection, "ActivityQuest_Flag", $"{{\"nId\":{activityId}}}", sync);
		}
	}
}
