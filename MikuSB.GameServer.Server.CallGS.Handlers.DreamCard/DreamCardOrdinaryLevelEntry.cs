using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DreamCard;

internal sealed class DreamCardOrdinaryLevelEntry
{
	[JsonPropertyName("LevelListID")]
	public uint LevelListId { get; set; }
}
