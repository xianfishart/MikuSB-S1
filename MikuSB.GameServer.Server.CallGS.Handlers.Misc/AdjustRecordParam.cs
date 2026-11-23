using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class AdjustRecordParam
{
	[JsonPropertyName("nType")]
	public uint Type { get; set; }
}
