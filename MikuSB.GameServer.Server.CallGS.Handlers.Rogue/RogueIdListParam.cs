using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

public sealed class RogueIdListParam
{
	[JsonPropertyName("tbIdList")]
	public List<uint> Ids { get; set; } = new List<uint>();
}
