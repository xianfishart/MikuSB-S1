using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class BirthdayShowParam
{
	[JsonPropertyName("bShow")]
	public bool Show { get; set; }
}
