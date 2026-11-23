using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

internal sealed class ChangeAccountShowParam
{
	[JsonPropertyName("nItemID")]
	public int ItemId { get; set; }

	[JsonPropertyName("nIndex")]
	public int Index { get; set; }
}
