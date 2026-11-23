using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Scene;

internal sealed class ChangeMainSceneParam
{
	[JsonPropertyName("nId")]
	public uint Id { get; set; }
}
