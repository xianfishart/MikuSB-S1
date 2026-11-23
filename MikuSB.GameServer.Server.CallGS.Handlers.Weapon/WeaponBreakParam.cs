using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

internal sealed class WeaponBreakParam
{
	[JsonPropertyName("Id")]
	public int WeaponId { get; set; }
}
