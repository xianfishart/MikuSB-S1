using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlCardUpdateLevelParam
{
	[JsonPropertyName("Id")]
	public int Id { get; set; }

	[JsonPropertyName("tbMaterials")]
	public List<GirlCardLevelMaterial> Materials { get; set; } = new List<GirlCardLevelMaterial>();
}
