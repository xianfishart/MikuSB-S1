using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

public sealed class JigsawGetRewardParam
{
	[JsonPropertyName("nActId")]
	public uint ActId { get; set; }

	[JsonPropertyName("nId")]
	public int TaskId { get; set; }
}
