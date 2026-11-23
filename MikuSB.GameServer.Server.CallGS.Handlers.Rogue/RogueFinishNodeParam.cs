using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

public sealed class RogueFinishNodeParam
{
	[JsonPropertyName("nID")]
	public uint NodeId { get; set; }

	[JsonPropertyName("nType")]
	public uint Type { get; set; }

	[JsonPropertyName("nLevelID")]
	public uint LevelId { get; set; }

	[JsonPropertyName("nState")]
	public uint State { get; set; }

	[JsonPropertyName("nSelectIndex")]
	public uint SelectIndex { get; set; }

	[JsonIgnore]
	public uint Node => NodeId;
}
