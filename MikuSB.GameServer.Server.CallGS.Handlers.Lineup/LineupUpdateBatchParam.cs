using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Lineup;

internal sealed class LineupUpdateBatchParam
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("index")]
	public int Index { get; set; }

	[JsonPropertyName("member1")]
	public uint Member1 { get; set; }

	[JsonPropertyName("member2")]
	public uint Member2 { get; set; }

	[JsonPropertyName("member3")]
	public uint Member3 { get; set; }
}
