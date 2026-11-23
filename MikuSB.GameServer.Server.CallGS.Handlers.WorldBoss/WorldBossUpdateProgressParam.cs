using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

public sealed class WorldBossUpdateProgressParam
{
	[JsonPropertyName("nID")]
	public uint ActivityId { get; set; }
}
