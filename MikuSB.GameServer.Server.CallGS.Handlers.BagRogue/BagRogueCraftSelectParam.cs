using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueCraftSelectParam
{
	[JsonPropertyName("nIndex")]
	public uint Index { get; set; }
}
