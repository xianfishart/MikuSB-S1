using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

[CallGSApi("DefendLogic_LevelSettlement")]
public class DefendLogic_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint levelId = DefendStore.ReadUint(request["nID"]);
		uint diff = DefendStore.ReadUint(request["nDiff"]);
		uint wave = DefendStore.ReadUint(request["nWave"]);
		uint score = DefendStore.ReadUint(request["nScore"]);
		bool success = request["bSuccess"]?.GetValue<bool>() ?? true;
		if (diff != 0)
		{
			DefendStore.SetAttr(player, 8u, diff, sync);
		}
		if (wave != 0)
		{
			PlayerAttr maxWave = DefendStore.GetOrCreate(player, 2u);
			if (wave > maxWave.Val)
			{
				maxWave.Val = wave;
				player.Attributes.SyncTo(sync, maxWave);
			}
		}
		if (success && score != 0)
		{
			PlayerAttr total = DefendStore.GetOrCreate(player, 5u);
			total.Val += score;
			player.Attributes.SyncTo(sync, total);
			PlayerAttr maxSingle = DefendStore.GetOrCreate(player, 7u);
			if (score > maxSingle.Val)
			{
				maxSingle.Val = score;
				player.Attributes.SyncTo(sync, maxSingle);
			}
		}
		if (levelId != 0)
		{
			uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			player.SetAttr(22u, levelId, now);
			sync.Custom[player.ToPackedAttrKey(22u, levelId)] = now;
			sync.Custom[player.ToShiftedAttrKey(22u, levelId)] = now;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "DefendLogic_LevelSettlement", "{}", sync);
	}
}
