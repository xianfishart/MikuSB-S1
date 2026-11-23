using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanUnlockEndParam
{
	[JsonPropertyName("nMapId")]
	public uint MapId { get; set; }

	[JsonPropertyName("nId")]
	public uint EndId { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
