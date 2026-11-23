using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class UpgradeMaterial
{
	[JsonPropertyName("Id")]
	public int Id { get; set; }

	[JsonPropertyName("Num")]
	public uint Num { get; set; }
}
