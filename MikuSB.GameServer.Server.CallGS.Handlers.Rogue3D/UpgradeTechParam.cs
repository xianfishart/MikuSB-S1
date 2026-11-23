using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

internal sealed class UpgradeTechParam
{
	[JsonPropertyName("nTechId")]
	public uint TechId { get; set; }
}
