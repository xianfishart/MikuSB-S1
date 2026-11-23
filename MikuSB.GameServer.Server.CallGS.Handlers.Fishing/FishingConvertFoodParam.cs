using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

internal sealed class FishingConvertFoodParam
{
	[JsonPropertyName("nFoodID")]
	public int FoodId { get; set; }

	[JsonPropertyName("nNum")]
	public uint Num { get; set; }
}
