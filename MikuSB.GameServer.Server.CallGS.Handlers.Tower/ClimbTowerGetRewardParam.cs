using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

internal sealed class ClimbTowerGetRewardParam
{
	[JsonPropertyName("nType")]
	public int? Type { get; set; }

	[JsonPropertyName("nLayer")]
	public int Layer { get; set; }

	[JsonPropertyName("nGroup")]
	public int? Group { get; set; }
}
