using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

internal sealed class NewPrologueSettlementParam
{
	[JsonPropertyName("nID")]
	public uint LevelId { get; set; }

	[JsonPropertyName("nTime")]
	public int Time { get; set; }

	[JsonPropertyName("bWaitServer")]
	public bool WaitServer { get; set; }

	[JsonPropertyName("tbLog")]
	public JsonNode? TbLog { get; set; }
}
