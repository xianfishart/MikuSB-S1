using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

internal sealed class ItemSelectParam
{
	[JsonPropertyName("nGroup")]
	public uint Group { get; set; }

	[JsonPropertyName("tbGDPLN")]
	public List<uint> Gdpln { get; set; } = new List<uint>();
}
