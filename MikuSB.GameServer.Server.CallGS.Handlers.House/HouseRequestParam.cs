using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal sealed class HouseRequestParam
{
	[JsonPropertyName("FuncName")]
	public string? FuncName { get; set; }
}
