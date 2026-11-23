using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardUpdateDataEntry
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("data")]
	public JsonElement Data { get; set; }
}
