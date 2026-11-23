using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

internal sealed class IbBuyGoodsParam
{
	[JsonPropertyName("nType")]
	public int Type { get; set; }

	[JsonPropertyName("nGoodsId")]
	public uint GoodsId { get; set; }

	[JsonPropertyName("nCount")]
	public uint Count { get; set; }

	[JsonPropertyName("nIndex")]
	public int Index { get; set; }

	[JsonPropertyName("tbSelectItem1")]
	public List<uint>? SelectItem1 { get; set; }

	[JsonPropertyName("tbSelectItem2")]
	public List<uint>? SelectItem2 { get; set; }
}
