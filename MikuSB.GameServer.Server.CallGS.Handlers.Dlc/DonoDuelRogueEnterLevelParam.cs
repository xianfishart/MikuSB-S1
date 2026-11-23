using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

public sealed class DonoDuelRogueEnterLevelParam
{
	[JsonPropertyName("nID")]
	public uint LevelId { get; set; }

	[JsonPropertyName("nTeamID")]
	public uint TeamId { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
