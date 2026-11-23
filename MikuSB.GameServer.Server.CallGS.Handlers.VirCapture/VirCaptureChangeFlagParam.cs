using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureChangeFlagParam
{
	[JsonPropertyName("nLevelID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nRegionId")]
	public int RegionId { get; set; }

	[JsonPropertyName("bClean")]
	public bool Clean { get; set; }
}
