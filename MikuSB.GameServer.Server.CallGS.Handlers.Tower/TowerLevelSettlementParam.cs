using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

internal sealed class TowerLevelSettlementParam
{
	[JsonPropertyName("nID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nTowerID")]
	public int TowerId { get; set; }

	[JsonPropertyName("nStar")]
	public int StarMask { get; set; }
}
