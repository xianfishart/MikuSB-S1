using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanVoteRewardParam
{
	[JsonPropertyName("nId")]
	public uint NpcId { get; set; }
}
