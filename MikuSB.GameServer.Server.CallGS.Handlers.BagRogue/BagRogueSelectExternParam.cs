using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueSelectExternParam
{
	[JsonPropertyName("tbUniqueId")]
	public List<uint>? UniqueIds { get; set; }
}
