using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

public sealed class ChessEnterMapParam
{
	[JsonPropertyName("nId")]
	public uint Id { get; set; }

	[JsonPropertyName("nMapId")]
	public uint MapId { get; set; }
}
