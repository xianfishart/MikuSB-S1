using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

public sealed class JigsawSetActiveParam
{
	[JsonPropertyName("nId")]
	public uint ScheduleId { get; set; }
}
