using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardSelectParam
{
	[JsonPropertyName("Id")]
	public int SupportCardUid { get; set; }

	[JsonPropertyName("SelectNew")]
	public bool SelectNew { get; set; }
}
