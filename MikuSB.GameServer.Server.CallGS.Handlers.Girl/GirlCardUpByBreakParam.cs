using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlCardUpByBreakParam
{
	[JsonPropertyName("pId")]
	public int CardId { get; set; }

	[JsonPropertyName("nBreakLv")]
	public uint BreakLv { get; set; }

	[JsonPropertyName("tbMaterials")]
	public List<List<int>> Materials { get; set; } = new List<List<int>>();
}
