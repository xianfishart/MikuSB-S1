using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueEquipParam
{
	[JsonPropertyName("tbList")]
	public List<JsonArray>? List { get; set; }

	[JsonPropertyName("tbTalent")]
	public List<JsonNode?>? Talents { get; set; }
}
