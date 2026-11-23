using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class RichmanLikeEndParam
{
	[JsonPropertyName("nId")]
	public uint EndId { get; set; }
}
