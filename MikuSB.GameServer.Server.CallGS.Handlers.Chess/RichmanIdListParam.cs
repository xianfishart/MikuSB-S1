using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanIdListParam
{
	[JsonPropertyName("tbIdList")]
	public List<uint> Ids { get; set; } = new List<uint>();
}
