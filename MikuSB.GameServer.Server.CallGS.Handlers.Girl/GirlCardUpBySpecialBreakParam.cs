using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlCardUpBySpecialBreakParam
{
	[JsonPropertyName("nCardId")]
	public int CardId { get; set; }
}
