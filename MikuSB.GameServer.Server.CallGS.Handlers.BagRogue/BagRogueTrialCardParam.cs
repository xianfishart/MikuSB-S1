using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueTrialCardParam
{
	[JsonPropertyName("tbTrial")]
	public List<uint>? Trials { get; set; }
}
