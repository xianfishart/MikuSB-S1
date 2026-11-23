using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal sealed class SupporterCardEquipParam
{
	[JsonPropertyName("EqId")]
	public int CardId { get; set; }

	[JsonPropertyName("beEqId")]
	public int SupportCardUid { get; set; }

	[JsonPropertyName("EqSlot")]
	public int EqSlot { get; set; }

	[JsonPropertyName("BEqId")]
	public int CurrentEquippedUid { get; set; }

	[JsonPropertyName("bForce")]
	public bool Force { get; set; }

	[JsonPropertyName("Model")]
	public string? Model { get; set; }
}
