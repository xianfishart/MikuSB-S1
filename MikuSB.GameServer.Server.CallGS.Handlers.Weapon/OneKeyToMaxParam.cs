using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

internal sealed class OneKeyToMaxParam
{
	[JsonPropertyName("Id")]
	public int Id { get; set; }

	[JsonPropertyName("tbBreakUpgradeMat")]
	public List<List<JsonElement>> TbBreakUpgradeMat { get; set; } = new List<List<JsonElement>>();
}
