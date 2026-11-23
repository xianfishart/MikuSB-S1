using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

public sealed class ChildNodeResetParam
{
	[JsonPropertyName("pId")]
	public int CardId { get; set; }
}
