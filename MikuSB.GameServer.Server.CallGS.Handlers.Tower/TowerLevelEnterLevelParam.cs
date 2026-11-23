using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

internal sealed class TowerLevelEnterLevelParam
{
	[JsonPropertyName("nID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nTeamID")]
	public int TeamId { get; set; }
}
