using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanSettlementParam
{
	[JsonPropertyName("nSeed")]
	public uint Seed { get; set; }

	[JsonPropertyName("activityId")]
	public uint ActivityId { get; set; }

	[JsonPropertyName("activityType")]
	public uint ActivityType { get; set; }

	[JsonPropertyName("mapId")]
	public uint MapId { get; set; }

	[JsonPropertyName("nRank")]
	public uint Rank { get; set; }

	[JsonPropertyName("tbLevelNum")]
	public JsonNode? LevelNum { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
