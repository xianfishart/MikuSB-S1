using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

public sealed class JigsawUseHelpParam
{
	[JsonPropertyName("nActId")]
	public uint ActId { get; set; }

	[JsonPropertyName("nId")]
	public uint PieceId { get; set; }
}
