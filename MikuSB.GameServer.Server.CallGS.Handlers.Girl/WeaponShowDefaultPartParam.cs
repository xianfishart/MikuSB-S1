using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class WeaponShowDefaultPartParam
{
	[JsonPropertyName("nFlag")]
	public int Flag { get; set; }

	public uint Id { get; set; }
}
