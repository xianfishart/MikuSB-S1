using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class ChildUnLockParam
{
	[JsonPropertyName("pId")]
	public int CardId { get; set; }

	[JsonPropertyName("tbInfo")]
	public NodeInfo? Info { get; set; }

	[JsonPropertyName("tbMat")]
	public List<List<int>> Materials { get; set; } = new List<List<int>>();
}
