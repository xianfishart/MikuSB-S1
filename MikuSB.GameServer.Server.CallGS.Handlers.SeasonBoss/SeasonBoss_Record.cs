using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.SeasonBoss;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SeasonBoss;

[CallGSApi("SeasonBoss_Record")]
public class SeasonBoss_Record : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = ReadUint(request["nID"]);
		int damage = (int)ReadUint(request["nDamage"]);
		int time = (int)ReadUint(request["nTime"]);
		bool record = request["bRecord"]?.GetValue<bool>() ?? true;
		if (levelId != 0)
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			SeasonBossProgressService.RecordResult(player, levelId, damage, time, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "SeasonBoss_Record", new JsonObject { ["bRecord"] = record }.ToJsonString());
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
