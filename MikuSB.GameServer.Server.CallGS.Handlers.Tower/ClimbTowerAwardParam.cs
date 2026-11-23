using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

public sealed class ClimbTowerAwardParam
{
	[JsonPropertyName("tbList")]
	public List<uint> Ids { get; set; } = new List<uint>();
}
