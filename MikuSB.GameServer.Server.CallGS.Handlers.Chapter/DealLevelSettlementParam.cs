using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

internal sealed class DealLevelSettlementParam
{
	[JsonPropertyName("sCmd")]
	public string? SCmd { get; set; }

	[JsonPropertyName("tbParam")]
	public JsonNode? TbParam { get; set; }
}
