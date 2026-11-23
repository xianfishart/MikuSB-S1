using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Nita;

public sealed class NitaMonolopyGameSettlementParam
{
	[JsonPropertyName("nMoney")]
	public uint Money { get; set; }

	[JsonPropertyName("nSumThrowCount")]
	public uint SumThrowCount { get; set; }

	[JsonPropertyName("nSumCellTriger")]
	public uint SumCellTriger { get; set; }

	[JsonPropertyName("sLog")]
	public string? Log { get; set; }
}
