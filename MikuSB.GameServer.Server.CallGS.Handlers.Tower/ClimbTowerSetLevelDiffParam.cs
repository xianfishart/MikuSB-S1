using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

internal sealed class ClimbTowerSetLevelDiffParam
{
	[JsonPropertyName("nDiff")]
	public int Diff { get; set; }
}
