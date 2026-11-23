using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueStoreBuyParam
{
	[JsonPropertyName("nSelectIdx")]
	public uint SelectIdx { get; set; }
}
