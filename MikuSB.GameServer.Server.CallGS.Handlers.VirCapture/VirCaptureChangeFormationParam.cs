using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureChangeFormationParam
{
	[JsonPropertyName("nId")]
	public int Id { get; set; }

	[JsonPropertyName("nUnloadId")]
	public int UnloadId { get; set; }
}
