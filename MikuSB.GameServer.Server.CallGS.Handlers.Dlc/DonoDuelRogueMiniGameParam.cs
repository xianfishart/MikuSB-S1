using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

public sealed class DonoDuelRogueMiniGameParam
{
	[JsonPropertyName("nSeed")]
	public uint Seed { get; set; }

	[JsonPropertyName("nRoomId")]
	public uint RoomId { get; set; }
}
