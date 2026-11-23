using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

internal sealed class GachaLaunchParam
{
	[JsonPropertyName("nId")]
	public int NId { get; set; }

	[JsonPropertyName("bPickUp")]
	public bool BPickUp { get; set; }

	[JsonPropertyName("nTime")]
	public int NTime { get; set; }
}
