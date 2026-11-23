using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

internal sealed class GirlSkinPartsUpdateParam
{
	[JsonPropertyName("tbPartsID")]
	public List<uint> PartsId { get; set; } = new List<uint>();

	[JsonPropertyName("nSkinId")]
	public uint SkinId { get; set; }
}
