using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureSavePosParam
{
	[JsonPropertyName("nLevelID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nPosX")]
	public int PosX { get; set; }

	[JsonPropertyName("nPosY")]
	public int PosY { get; set; }

	[JsonPropertyName("nPosZ")]
	public int PosZ { get; set; }

	[JsonPropertyName("nToward")]
	public int Toward { get; set; }
}
