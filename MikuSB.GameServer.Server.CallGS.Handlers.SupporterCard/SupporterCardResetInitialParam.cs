using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardResetInitialParam
{
	[JsonPropertyName("Id")]
	public int SupportCardUid { get; set; }

	[JsonPropertyName("Index")]
	public int Index { get; set; }

	[JsonPropertyName("FixedId")]
	public uint FixedId { get; set; }
}
