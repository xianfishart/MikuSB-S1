using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

public sealed class FarmWarGrowParam
{
	[JsonPropertyName("nId")]
	public uint Id { get; set; }

	[JsonPropertyName("nSkillId")]
	public uint SkillId { get; set; }

	[JsonPropertyName("nType")]
	public uint Type { get; set; }
}
