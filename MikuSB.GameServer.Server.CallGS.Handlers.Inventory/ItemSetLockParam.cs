using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

/// <summary>
/// Item_SetLock 请求体。
/// 客户端 uw_logistics_tips_item3:SetLock 发的是
///   me:CallGS("Item_SetLock", json.encode({ ItemId = pItem:Id(), Lock = not hasLock }))
/// 所以字段名必须严格是 ItemId / Lock，Lock 是布尔。
/// </summary>
internal sealed class ItemSetLockParam
{
	[JsonPropertyName("ItemId")]
	public long ItemId { get; set; }

	[JsonPropertyName("Lock")]
	public bool Lock { get; set; }
}
