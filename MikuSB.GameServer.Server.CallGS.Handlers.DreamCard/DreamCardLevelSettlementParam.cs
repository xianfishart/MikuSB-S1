using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardLevelSettlementParam
{
	[JsonPropertyName("nID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nDiff")]
	public int Diff { get; set; }

	[JsonPropertyName("nType")]
	public int Type { get; set; }

	[JsonPropertyName("nStarValue")]
	public int StarValue { get; set; }

	[JsonPropertyName("nScore")]
	public int Score { get; set; }

	/// <summary>第 494 节：本局获得棋子的星级分布（客户端 CardStarCount）。</summary>
	[JsonPropertyName("CardStarCount")]
	public JsonNode? CardStarCount { get; set; }

	/// <summary>第 494 节：本局获得礼物的星级分布（客户端 TreStarCount）。</summary>
	[JsonPropertyName("TreStarCount")]
	public JsonNode? TreStarCount { get; set; }

	[JsonPropertyName("CardStarCount2")]
	public JsonNode? CardStarCount2 { get; set; }

	[JsonPropertyName("TreStarCount2")]
	public JsonNode? TreStarCount2 { get; set; }

	/// <summary>第 494 节：本局消耗/销毁的棋子数。</summary>
	[JsonPropertyName("DestroyCount")]
	public JsonNode? DestroyCount { get; set; }

	/// <summary>第 494 节：本局推进到第几层。</summary>
	[JsonPropertyName("nStage")]
	public int Stage { get; set; }

	/// <summary>第 494 节：本局最终漫溯等级。</summary>
	[JsonPropertyName("nLevel")]
	public int Level { get; set; }

	/// <summary>第 494 节：各流派累计漫溯能量（流派 ID → 能量）。</summary>
	[JsonPropertyName("BuildEnergy")]
	public JsonNode? BuildEnergy { get; set; }

	/// <summary>第 494 节：无尽模式各流派本次提升分。</summary>
	[JsonPropertyName("tbUpScore")]
	public JsonNode? UpScore { get; set; }
}
