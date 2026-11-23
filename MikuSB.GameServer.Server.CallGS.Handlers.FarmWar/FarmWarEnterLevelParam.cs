using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

public sealed class FarmWarEnterLevelParam
{
	[JsonPropertyName("nID")]
	public uint Id { get; set; }

	[JsonPropertyName("nTeamID")]
	public uint TeamId { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
