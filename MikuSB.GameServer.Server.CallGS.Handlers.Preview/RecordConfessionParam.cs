using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Preview;

internal sealed class RecordConfessionParam
{
	[JsonPropertyName("nIdx")]
	public uint Id { get; set; }
}
