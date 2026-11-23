using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("DefendLogic_SelectDiff")]
public class DefendLogic_SelectDiff : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int diff = 1;
		try
		{
			diff = JsonNode.Parse(param)?["nDiffId"]?.GetValue<int>() ?? 1;
		}
		catch
		{
		}
		if (diff <= 0)
		{
			diff = 1;
		}
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 10 && x.Sid == 8);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 10u,
				Sid = 8u,
				Val = (uint)diff
			};
			player.Data.Attrs.Add(attr);
		}
		else
		{
			attr.Val = (uint)diff;
		}
		sync.Custom[player.ToPackedAttrKey(10u, 8u)] = (uint)diff;
		sync.Custom[player.ToShiftedAttrKey(10u, 8u)] = (uint)diff;
		await CallGSRouter.SendScript(connection, "DefendLogic_SelectDiff", "{}", sync);
	}
}
