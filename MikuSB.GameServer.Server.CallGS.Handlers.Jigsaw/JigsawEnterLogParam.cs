using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

public sealed class JigsawEnterLogParam
{
	[JsonPropertyName("nId")]
	public uint ScheduleId { get; set; }

	[JsonPropertyName("nEnterType")]
	public uint EnterType { get; set; }
}
