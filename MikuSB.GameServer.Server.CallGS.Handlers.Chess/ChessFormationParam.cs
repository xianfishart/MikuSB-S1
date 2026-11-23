using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class ChessFormationParam
{
	[JsonPropertyName("tbFormation")]
	public JsonNode? Formation { get; set; }
}
