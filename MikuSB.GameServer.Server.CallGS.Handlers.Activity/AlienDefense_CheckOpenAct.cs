using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("AlienDefense_CheckOpenAct")]
public class AlienDefense_CheckOpenAct : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint nId = player.GetAttr(143u, 1u);
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty("nId", out var node) && node.TryGetInt32(out var requested) && requested > 0)
			{
				nId = (uint)requested;
			}
		}
		catch
		{
		}
		if (nId == 0)
		{
			nId = 4u;
		}
		SetAttr(player, 143u, 1u, nId, sync);
		await CallGSRouter.SendScript(connection, "AlienDefense_CheckOpenAct", "{\"bOpen\":true,\"nId\":" + nId + ",\"nStartTime\":1767211200,\"nEndTime\":2145816000}", sync);
	}

	private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}
}
