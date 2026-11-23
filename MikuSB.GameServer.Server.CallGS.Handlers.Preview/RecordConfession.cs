using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Preview;

[CallGSApi("RecordConfession")]
public class RecordConfession : ICallGSHandler
{
	private const int MainSceneGID = 132;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		RecordConfessionParam req = JsonSerializer.Deserialize<RecordConfessionParam>(param);
		if (req != null)
		{
			uint sid = req.Id + 10;
			PlayerInstance player = connection.Player;
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 132 && x.Sid == sid);
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = 132u,
					Sid = sid,
					Val = 1u
				};
				player.Data.Attrs.Add(attr);
			}
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.Custom[player.ToPackedAttrKey(132u, sid)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(132u, sid)] = attr.Val;
			await CallGSRouter.SendScript(connection, "RecordConfession", "{}", sync);
		}
	}
}
