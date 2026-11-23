using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTowerLogic_SetLevelDiff")]
public class ClimbTowerLogic_SetLevelDiff : ICallGSHandler
{
	private const uint TowerGroupId = 3u;

	private const uint DiffSid = 4u;

	private const uint HisDiffSid = 5u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ClimbTowerSetLevelDiffParam req = JsonSerializer.Deserialize<ClimbTowerSetLevelDiffParam>(param);
		if (req == null || req.Diff <= 0)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_SetLevelDiff", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.ClimbTowerDiffData.ContainsKey((uint)req.Diff))
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_SetLevelDiff", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint hisDiff = GetAttrValue(player.Data, 3u, 5u);
		if (req.Diff > hisDiff + 1)
		{
			await CallGSRouter.SendScript(connection, "ClimbTowerLogic_SetLevelDiff", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerAttr diffAttr = GetOrCreateAttr(player.Data, 3u, 4u);
		diffAttr.Val = (uint)req.Diff;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Custom[player.ToPackedAttrKey(diffAttr.Gid, diffAttr.Sid)] = diffAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(diffAttr.Gid, diffAttr.Sid)] = diffAttr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "ClimbTowerLogic_SetLevelDiff", "{}", sync);
	}

	private static uint GetAttrValue(PlayerGameData data, uint gid, uint sid)
	{
		return data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid)?.Val ?? 0;
	}

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}
}
