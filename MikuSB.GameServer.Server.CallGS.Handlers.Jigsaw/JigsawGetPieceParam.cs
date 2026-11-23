using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

public sealed class JigsawGetPieceParam
{
	[JsonPropertyName("nId")]
	public uint ScheduleId { get; set; }

	[JsonPropertyName("nStage")]
	public uint Stage { get; set; }

	[JsonPropertyName("nNum")]
	public uint Count { get; set; }

	[JsonPropertyName("nActivityId")]
	public uint ActivityId { get; set; }
}
