using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueUpgradeParam
{
	[JsonPropertyName("nUniqueA")]
	public uint UniqueA { get; set; }

	[JsonPropertyName("nUniqueB")]
	public uint UniqueB { get; set; }
}
