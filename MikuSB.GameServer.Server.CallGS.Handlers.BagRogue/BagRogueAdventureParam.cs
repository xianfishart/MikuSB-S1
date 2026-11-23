using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

public sealed class BagRogueAdventureParam
{
	[JsonPropertyName("nSelectIdx")]
	public int SelectIdx { get; set; }
}
