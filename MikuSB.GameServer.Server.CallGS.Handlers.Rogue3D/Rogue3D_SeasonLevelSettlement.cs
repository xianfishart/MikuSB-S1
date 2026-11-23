using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_SeasonLevelSettlement")]
public class Rogue3D_SeasonLevelSettlement : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint DangerMaxSid = 3001u;

	private const uint InfinityPointMaxSid = 3002u;

	private const uint NodeIdSid = 3003u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		JsonObject fight = request["tbFightData"] as JsonObject;
		uint danger = ReadUint(fight?["danger_value"]);
		uint infinity = ReadUint(fight?["infinity_point"]);
		uint nodeId = ReadUint(fight?["nodeId"]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint newScore = UpdateMax(player, 3001u, danger, sync);
		UpdateMax(player, 3002u, infinity, sync);
		if (nodeId != 0)
		{
			UpdateMax(player, 3003u, nodeId, sync);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject response = new JsonObject
		{
			["tbAward"] = new JsonArray(),
			["tbMonsterDropRiki"] = new JsonArray(),
			["nNewScore"] = newScore,
			["nOldScore"] = newScore,
			["tbChallengeMap"] = new JsonObject()
		};
		await CallGSRouter.SendScript(connection, "Rogue3D_SeasonLevelSettlement", response.ToJsonString(), sync);
	}

	private static uint UpdateMax(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(124u, sid);
		if (value > orCreate.Val)
		{
			orCreate.Val = value;
			player.Attributes.SyncTo(sync, orCreate);
		}
		return orCreate.Val;
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
