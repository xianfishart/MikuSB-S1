using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal sealed class VirCaptureGetLevelAwardParam
{
	[JsonPropertyName("nId")]
	public int ActId { get; set; }

	[JsonPropertyName("tbIdList")]
	public List<int> IdList { get; set; } = new List<int>();
}
