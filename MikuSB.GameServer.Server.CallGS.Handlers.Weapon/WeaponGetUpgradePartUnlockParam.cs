using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

public sealed class WeaponGetUpgradePartUnlockParam
{
	[JsonPropertyName("gdpl")]
	public List<int> PartGdpl { get; set; } = new List<int>();

	[JsonPropertyName("weapon")]
	public string WeaponGdpl { get; set; } = "";

	[JsonPropertyName("nWeaponId")]
	public int WeaponId { get; set; }
}
