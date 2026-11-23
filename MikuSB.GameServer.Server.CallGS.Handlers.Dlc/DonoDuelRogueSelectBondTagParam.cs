using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

public sealed class DonoDuelRogueSelectBondTagParam
{
	[JsonPropertyName("nBondTag")]
	public uint BondTag { get; set; }
}
