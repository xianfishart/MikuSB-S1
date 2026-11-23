using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("Adjust_Record")]
public class Adjust_Record : ICallGSHandler
{
	private const uint GroupId = 107u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		AdjustRecordParam req = JsonSerializer.Deserialize<AdjustRecordParam>(param);
		if (req == null || req.Type == 0)
		{
			await CallGSRouter.SendScript(connection, "Adjust_Record", "null");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = GetOrCreateAttr(player, req.Type);
		if (attr.Val == 0)
		{
			attr.Val = 1u;
			sync.Custom[player.ToPackedAttrKey(107u, req.Type)] = 1u;
			sync.Custom[player.ToShiftedAttrKey(107u, req.Type)] = 1u;
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "Adjust_Record", "null", sync);
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 107 && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = 107u,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}
}
