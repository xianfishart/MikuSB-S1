using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

public sealed class RogueFormationParam
{
	[JsonPropertyName("tbFormation")]
	public JsonArray? Formation { get; set; }
}
