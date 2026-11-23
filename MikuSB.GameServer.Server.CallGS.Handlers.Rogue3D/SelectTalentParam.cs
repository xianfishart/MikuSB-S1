using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal sealed class SelectTalentParam
{
	[JsonPropertyName("nTalentId")]
	public uint TalentId { get; set; }
}
