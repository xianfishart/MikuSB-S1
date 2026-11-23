using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

public sealed class TokenBoxUseParam
{
	[JsonPropertyName("tbList")]
	public List<uint>? TbList { get; set; }
}
