using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

internal sealed class GetRewardParam
{
	[JsonPropertyName("nId")]
	public int Id { get; set; }
}
