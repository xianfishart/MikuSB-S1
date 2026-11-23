using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

public sealed class ClimbTowerEnterLevelParam
{
	[JsonPropertyName("nId")]
	public uint Id { get; set; }

	[JsonPropertyName("nBuffId")]
	public uint BuffId { get; set; }

	[JsonPropertyName("nTeamId")]
	public uint TeamId { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
