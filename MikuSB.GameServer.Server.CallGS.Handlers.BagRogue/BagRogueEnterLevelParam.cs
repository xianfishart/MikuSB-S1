using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueEnterLevelParam
{
	[JsonPropertyName("nID")]
	public uint Id { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
