using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueQuestAwardParam
{
	[JsonPropertyName("tbIdList")]
	public List<uint>? Ids { get; set; }
}
