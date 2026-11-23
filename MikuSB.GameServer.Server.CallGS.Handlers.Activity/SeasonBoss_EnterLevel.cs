using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("SeasonBoss_EnterLevel")]
public class SeasonBoss_EnterLevel : ICallGSHandler
{
	private static readonly Random Random = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			JsonObject tb;
			try
			{
				tb = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject;
			}
			catch
			{
				tb = null;
			}
			uint levelId = ReadNum(tb?["nID"]);
			uint lineupId = ReadNum(tb?["nTeamID"]);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			SeasonBossProgressService.RecordLineup(player, levelId, lineupId, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			await CallGSRouter.SendScript(connection, "SeasonBoss_EnterLevel", $"{{\"nSeed\":{Random.Next(1, 1000000000)}}}", sync);
		}
	}

	private static uint ReadNum(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<double>(out var value))
			{
				return (uint)Math.Max(0.0, value);
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (uint)Math.Max(0, value2);
			}
		}
		return 0u;
	}
}
