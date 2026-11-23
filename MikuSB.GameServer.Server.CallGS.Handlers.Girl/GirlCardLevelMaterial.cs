using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlCardLevelMaterial
{
	[JsonPropertyName("Id")]
	public int Id { get; set; }

	[JsonPropertyName("Num")]
	public uint Num { get; set; }
}
