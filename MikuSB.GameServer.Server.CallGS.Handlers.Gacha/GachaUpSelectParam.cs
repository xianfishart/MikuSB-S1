using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

internal sealed class GachaUpSelectParam
{
	[JsonPropertyName("nId")]
	public int NId { get; set; }

	[JsonPropertyName("gdpl")]
	public List<uint>? Gdpl { get; set; }
}
