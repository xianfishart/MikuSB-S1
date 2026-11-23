using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("AchievementExtend_RefreshPopFlag")]
public class AchievementExtend_RefreshPopFlag : ICallGSHandler
{
	private const uint PopGid = 111u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonNode jsonNode = (JsonNode.Parse(param)?.AsObject())?["tbList"];
		if (!(jsonNode is JsonArray list))
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (JsonNode node in list)
		{
			if (int.TryParse(node?.ToString(), out var id) && id > 0)
			{
				uint sid = (uint)(id / 30 + 1);
				int bit = id % 30;
				PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 111 && x.Sid == sid);
				if (attr == null)
				{
					attr = new PlayerAttr
					{
						Gid = 111u,
						Sid = sid,
						Val = 0u
					};
					player.Data.Attrs.Add(attr);
				}
				attr.Val |= (uint)(1 << bit);
				sync.Custom[player.ToPackedAttrKey(111u, sid)] = attr.Val;
				sync.Custom[player.ToShiftedAttrKey(111u, sid)] = attr.Val;
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "AchievementExtend_RefreshPopFlag", "{}", sync);
	}
}
