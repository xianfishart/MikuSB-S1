using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardEnterLevelParam
{
	[JsonPropertyName("nID")]
	public int LevelId { get; set; }

	[JsonPropertyName("nDiff")]
	public int Diff { get; set; }

	[JsonPropertyName("nType")]
	public int Type { get; set; }

	[JsonPropertyName("nRoleId")]
	public int RoleId { get; set; }
}
