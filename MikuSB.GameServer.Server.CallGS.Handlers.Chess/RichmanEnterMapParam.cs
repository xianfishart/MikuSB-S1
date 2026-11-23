using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanEnterMapParam
{
	[JsonPropertyName("nId")]
	public uint Id { get; set; }

	[JsonPropertyName("nMapId")]
	public uint MapId { get; set; }

	[JsonPropertyName("bContinue")]
	public bool Continue { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? Log { get; set; }
}
