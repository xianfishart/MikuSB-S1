using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class ChessEnterLevelParam
{
	[JsonPropertyName("activityId")]
	public uint ActivityId { get; set; }

	[JsonPropertyName("activityType")]
	public uint ActivityType { get; set; }

	[JsonPropertyName("moduleName")]
	public string? Module { get; set; }

	[JsonPropertyName("fightId")]
	public uint FightId { get; set; }

	[JsonPropertyName("nTeamID")]
	public uint TeamId { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
