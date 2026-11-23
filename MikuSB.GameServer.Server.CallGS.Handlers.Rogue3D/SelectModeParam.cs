using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal sealed class SelectModeParam
{
	[JsonPropertyName("nModeID")]
	public uint ModeId { get; set; }
}
