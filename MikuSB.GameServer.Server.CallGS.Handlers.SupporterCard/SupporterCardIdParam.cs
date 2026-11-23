using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardIdParam
{
	[JsonPropertyName("Id")]
	public int SupportCardUid { get; set; }
}
