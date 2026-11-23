using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

internal sealed class WeaponUpgradeParam
{
	[JsonPropertyName("Id")]
	public int Id { get; set; }

	[JsonPropertyName("tbMat")]
	public List<List<int>> TbMat { get; set; } = new List<List<int>>();
}
