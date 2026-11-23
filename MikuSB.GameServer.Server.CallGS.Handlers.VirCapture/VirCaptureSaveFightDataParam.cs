using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureSaveFightDataParam
{
	[JsonPropertyName("nLevelID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nRegionId")]
	public int RegionId { get; set; }
}
