using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

internal sealed class ItemOpenBoxParam
{
	[JsonPropertyName("Id")]
	public uint Id { get; set; }

	[JsonPropertyName("Count")]
	public uint Count { get; set; }

	[JsonPropertyName("tbSelect")]
	public List<ItemSelectParam>? Select { get; set; }
}
