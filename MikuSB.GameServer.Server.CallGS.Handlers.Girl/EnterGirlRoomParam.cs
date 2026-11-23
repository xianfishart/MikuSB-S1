using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class EnterGirlRoomParam
{
	[JsonPropertyName("nSkinId")]
	public int SkinId { get; set; }

	[JsonPropertyName("nCardId")]
	public uint CardId { get; set; }
}
