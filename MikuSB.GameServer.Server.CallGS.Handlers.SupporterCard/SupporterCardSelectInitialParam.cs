using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardSelectInitialParam
{
	[JsonPropertyName("Id")]
	public int SupportCardUid { get; set; }

	[JsonPropertyName("Index")]
	public int Index { get; set; }

	[JsonPropertyName("SelectNew")]
	public bool SelectNew { get; set; }
}
