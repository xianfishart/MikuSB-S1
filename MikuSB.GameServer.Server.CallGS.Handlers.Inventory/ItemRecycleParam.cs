using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

internal sealed class ItemRecycleParam
{
	[JsonPropertyName("tbItems")]
	public List<int> TbItems { get; set; } = new List<int>();
}
