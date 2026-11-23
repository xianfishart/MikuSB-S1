using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

public sealed class NitaMonolopyGetTreeAwardParam
{
	[JsonPropertyName("nTreeIdx")]
	public uint TreeIdx { get; set; }
}
