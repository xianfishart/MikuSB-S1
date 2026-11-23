using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class SetRoleLikeFlagParam
{
	[JsonPropertyName("nFlag")]
	public int Flag { get; set; }

	[JsonPropertyName("nCardID")]
	public uint CardId { get; set; }
}
