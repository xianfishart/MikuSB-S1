using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueSaveExternParam
{
	[JsonPropertyName("nUniqueId")]
	public uint UniqueId { get; set; }

	[JsonPropertyName("nNodeId")]
	public uint NodeId { get; set; }
}
