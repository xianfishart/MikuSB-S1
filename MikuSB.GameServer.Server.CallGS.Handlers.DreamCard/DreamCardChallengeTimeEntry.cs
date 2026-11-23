using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardChallengeTimeEntry
{
	[JsonPropertyName("ChallTimeID")]
	public uint ChallTimeId { get; set; }

	[JsonPropertyName("StartTime")]
	public string StartTime { get; set; } = "";

	[JsonPropertyName("EndTime")]
	public string EndTime { get; set; } = "";
}
