using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

public sealed class WeaponExchangeParam
{
	[JsonPropertyName("CardIdA")]
	public int CardIdA { get; set; }

	[JsonPropertyName("CardIdB")]
	public int CardIdB { get; set; }
}
