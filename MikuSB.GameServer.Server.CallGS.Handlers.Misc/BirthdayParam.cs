using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class BirthdayParam
{
	[JsonPropertyName("nMouth")]
	public int Month { get; set; }

	[JsonPropertyName("nDay")]
	public int Day { get; set; }
}
