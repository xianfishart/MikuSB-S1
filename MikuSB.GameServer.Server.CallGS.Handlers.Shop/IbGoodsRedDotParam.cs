using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

internal sealed class IbGoodsRedDotParam
{
	[JsonPropertyName("tbList")]
	public List<uint> GoodsIds { get; set; } = new List<uint>();
}
