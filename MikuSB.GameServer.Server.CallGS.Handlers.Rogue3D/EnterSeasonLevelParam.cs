using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal sealed class EnterSeasonLevelParam
{
	[JsonPropertyName("nDiffId")]
	public uint DiffId { get; set; }
}
