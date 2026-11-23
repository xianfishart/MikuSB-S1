using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_EnterSeasonLevel")]
public class Rogue3D_EnterSeasonLevel : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint SeasonGameplayIdSid = 1006u;

	private const uint SeasonEnterFlagSid = 1008u;

	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		EnterSeasonLevelParam req = JsonSerializer.Deserialize<EnterSeasonLevelParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_EnterSeasonLevel", "{\"nSeed\":0}");
			return;
		}
		if (!GameData.Rogue3DDifficultData.TryGetValue(req.DiffId, out Rogue3DDifficultExcel cfg) || cfg.GameplayGroup.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Rogue3D_EnterSeasonLevel", "{\"sErr\":\"rogue3.massage_gameProcessError\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		SetAttr(player, 1006u, cfg.GameplayGroup[0], sync);
		SetAttr(player, 1008u, 1u, sync);
		int seed = Random.Next(1, 1000000000);
		await CallGSRouter.SendScript(connection, "Rogue3D_EnterSeasonLevel", $"{{\"nSeed\":{seed}}}", sync);
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
		if (playerAttr.Val != val)
		{
			playerAttr.Val = val;
			sync.Custom[player.ToPackedAttrKey(124u, sid)] = val;
			sync.Custom[player.ToShiftedAttrKey(124u, sid)] = val;
		}
	}
}
