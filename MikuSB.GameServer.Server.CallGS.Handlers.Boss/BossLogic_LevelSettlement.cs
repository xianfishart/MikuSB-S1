using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Boss;

[CallGSApi("BossLogic_LevelSettlement")]
public class BossLogic_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = BossStore.ReadUint(request["nID"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (levelId != 0)
		{
			int diff = (int)BossStore.ReadUint(request["nDiff"]);
			List<uint> entryIds = new List<uint>();
			JsonNode jsonNode = request["tbEntrie"];
			if (jsonNode is JsonArray entries)
			{
				foreach (JsonNode entry in entries)
				{
					uint id = BossStore.ReadUint(entry);
					if (id != 0)
					{
						entryIds.Add(id);
					}
				}
			}
			uint score = BossStore.ComputeScore(levelId, diff, entryIds);
			uint sid = BossStore.ScoreSid(levelId);
			uint previous = (uint.TryParse(player.Attributes.GetStringValue(50u, sid), out var old) ? old : 0u);
			if (score > previous)
			{
				PlayerStrAttr attr = player.Attributes.SetString(50u, sid, score.ToString());
				player.Attributes.SyncTo(sync, attr);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "BossLogic_LevelSettlement", "{}", sync);
	}
}
