using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class ChangeSkinTypeParam
{
	[JsonPropertyName("nType")]
	public uint Type { get; set; }

	[JsonPropertyName("nSkinId")]
	public uint SkinId { get; set; }
}
