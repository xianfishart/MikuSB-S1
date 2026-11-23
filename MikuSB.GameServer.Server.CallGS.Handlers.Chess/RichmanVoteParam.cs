using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanVoteParam
{
	[JsonPropertyName("nId")]
	public uint NpcId { get; set; }

	[JsonPropertyName("nNum")]
	public uint Count { get; set; }

	[JsonPropertyName("nTotalNum")]
	public uint TotalCount { get; set; }
}
