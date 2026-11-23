using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureTowerEnterLevelParam
{
	[JsonPropertyName("nID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nTeamID")]
	public int TeamId { get; set; }
}
