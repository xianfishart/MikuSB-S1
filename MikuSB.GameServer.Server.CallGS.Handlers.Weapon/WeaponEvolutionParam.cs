using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

internal sealed class WeaponEvolutionParam
{
	[JsonPropertyName("Id")]
	public int WeaponId { get; set; }

	[JsonPropertyName("nItemId")]
	public int MaterialId { get; set; }
}
