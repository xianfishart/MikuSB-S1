using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class SetShowBadgeParam
{
	[JsonPropertyName("tbBadge")]
	public List<uint> Badges { get; set; } = new List<uint>();
}
