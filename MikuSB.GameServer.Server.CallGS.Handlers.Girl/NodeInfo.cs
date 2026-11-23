using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class NodeInfo
{
	[JsonPropertyName("Indx")]
	public int Indx { get; set; }

	[JsonPropertyName("InSubIdx")]
	public int InSubIdx { get; set; }
}
