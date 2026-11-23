using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

public sealed class VigourSupplyReceiveParam
{
	[JsonPropertyName("nId")]
	public uint Id { get; set; }
}
