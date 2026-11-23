using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlWeaponSkinParam
{
	[JsonPropertyName("nCardId")]
	public uint CardId { get; set; }

	[JsonPropertyName("nSkinId")]
	public uint SkinId { get; set; }
}
