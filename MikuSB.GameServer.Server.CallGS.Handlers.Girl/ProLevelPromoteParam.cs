using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class ProLevelPromoteParam
{
	[JsonPropertyName("nID")]
	public int CardId { get; set; }
}
