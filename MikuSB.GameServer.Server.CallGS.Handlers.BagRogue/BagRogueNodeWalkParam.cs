using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueNodeWalkParam
{
	[JsonPropertyName("nNodeId")]
	public uint NodeId { get; set; }
}
