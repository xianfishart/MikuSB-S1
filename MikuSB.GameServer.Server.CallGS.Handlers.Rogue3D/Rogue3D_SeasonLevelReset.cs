using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_SeasonLevelReset")]
public class Rogue3D_SeasonLevelReset : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint SeasonEnterFlagSid = 1008u;

	private const uint SeasonGameplayIdSid = 1006u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		SetAttr(player, 1008u, 0u, sync);
		SetAttr(player, 1006u, 0u, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject response = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		response["nSeed"] = Random.Shared.Next(1, 1000000000);
		response["bReset"] = true;
		await CallGSRouter.SendScript(connection, "Rogue3D_SeasonLevelReset", response.ToJsonString(), sync);
	}

	private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
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
		playerAttr.Val = value;
		sync.Custom[player.ToPackedAttrKey(124u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(124u, sid)] = value;
	}
}
