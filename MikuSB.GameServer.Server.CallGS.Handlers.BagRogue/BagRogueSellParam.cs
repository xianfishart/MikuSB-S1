using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueSellParam
{
	[JsonPropertyName("nUniqueId")]
	public uint UniqueId { get; set; }
}
