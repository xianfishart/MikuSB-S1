using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureEnterLevelParam
{
	[JsonPropertyName("nLevelID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nTeamID")]
	public int TeamId { get; set; }
}
