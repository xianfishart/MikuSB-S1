using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureSaveCaptureParam
{
	[JsonPropertyName("nLevelID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nRegionId")]
	public int RegionId { get; set; }
}
