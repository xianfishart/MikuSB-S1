using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class SetSupporterTeamIndexParam
{
	[JsonPropertyName("Id")]
	public uint CardId { get; set; }

	public uint Index { get; set; }
}
