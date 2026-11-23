using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardOrdinarySettlementEntry
{
	[JsonPropertyName("LevelListID")]
	public uint LevelListId { get; set; }

	[JsonPropertyName("HardStage")]
	public uint HardStage { get; set; }

	[JsonPropertyName("RewardID")]
	public uint RewardId { get; set; }
}
