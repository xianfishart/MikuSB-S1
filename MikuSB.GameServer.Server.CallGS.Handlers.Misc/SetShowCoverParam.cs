using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class SetShowCoverParam
{
	[JsonPropertyName("nID")]
	public uint Id { get; set; }
}
