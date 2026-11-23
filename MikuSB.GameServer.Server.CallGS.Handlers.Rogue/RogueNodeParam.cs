using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

public sealed class RogueNodeParam
{
	[JsonPropertyName("nID")]
	public uint NodeId { get; set; }

	[JsonPropertyName("nNodeID")]
	public uint NodeIdAlt { get; set; }

	[JsonIgnore]
	public uint Node => (NodeId != 0) ? NodeId : NodeIdAlt;
}
