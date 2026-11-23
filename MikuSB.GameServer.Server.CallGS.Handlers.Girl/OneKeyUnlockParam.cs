using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class OneKeyUnlockParam
{
	[JsonPropertyName("pId")]
	public int CardId { get; set; }

	[JsonPropertyName("nIdx")]
	public int MastIdx { get; set; }

	[JsonPropertyName("tbOneKey")]
	public List<int> SubIdxList { get; set; } = new List<int>();
}
