using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_SelectDiff")]
public class Rogue3D_SelectDiff : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint CurDiffSid = 5u;

	private const uint GameplayIdSid = 6u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		SelectDiffParam req = JsonSerializer.Deserialize<SelectDiffParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_SelectDiff", "{}");
			return;
		}
		if (!GameData.Rogue3DDifficultData.TryGetValue(req.DiffId, out Rogue3DDifficultExcel cfg) || cfg.GameplayGroup.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_SelectDiff", "{\"sErr\":\"rogue3.massage_gameProcessError\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		SetAttr(player, 5u, req.DiffId, sync);
		SetAttr(player, 6u, cfg.GameplayGroup[0], sync);
		await CallGSRouter.SendScript(connection, "Rogue3D_SelectDiff", "{}", sync);
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint val, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 124u,
				Sid = sid
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val = val;
		sync.Custom[player.ToPackedAttrKey(124u, sid)] = val;
		sync.Custom[player.ToShiftedAttrKey(124u, sid)] = val;
	}
}
