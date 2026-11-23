using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

public sealed class FarmWarUpdateFormationParam
{
	[JsonPropertyName("nLevelId")]
	public uint LevelId { get; set; }

	[JsonPropertyName("tbTeam")]
	public JsonArray? Team { get; set; }
}
