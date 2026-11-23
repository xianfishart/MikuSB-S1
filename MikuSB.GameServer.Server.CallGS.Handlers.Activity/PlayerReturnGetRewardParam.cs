using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

public sealed class PlayerReturnGetRewardParam
{
	[JsonPropertyName("nId")]
	public uint ActId { get; set; }

	[JsonPropertyName("nType")]
	public uint Type { get; set; }

	[JsonPropertyName("bQuick")]
	public bool Quick { get; set; }

	[JsonPropertyName("missionId")]
	public uint MissionId { get; set; }
}
