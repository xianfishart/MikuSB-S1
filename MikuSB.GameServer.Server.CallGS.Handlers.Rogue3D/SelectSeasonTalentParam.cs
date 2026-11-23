using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal sealed class SelectSeasonTalentParam
{
	[JsonPropertyName("nTalentId")]
	public uint TalentId { get; set; }
}
