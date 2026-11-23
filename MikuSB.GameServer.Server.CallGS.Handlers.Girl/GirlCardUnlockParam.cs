using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlCardUnlockParam
{
	[JsonPropertyName("g")]
	public uint Genre { get; set; }

	[JsonPropertyName("d")]
	public uint Detail { get; set; }

	[JsonPropertyName("p")]
	public uint Particular { get; set; }

	[JsonPropertyName("l")]
	public uint Level { get; set; }
}
