using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

internal sealed class ClimbTowerRecordProgressParam
{
	[JsonPropertyName("nID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nArea")]
	public int Area { get; set; }

	[JsonPropertyName("nStar")]
	public int StarMask { get; set; }

	[JsonPropertyName("tbRoleHP")]
	public List<List<int>> RoleHP { get; set; } = new List<List<int>>();

	[JsonPropertyName("nTeamEnergy")]
	public int? TeamEnergy { get; set; }
}
