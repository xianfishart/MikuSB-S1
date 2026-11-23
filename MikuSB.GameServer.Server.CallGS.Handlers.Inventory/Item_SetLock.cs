using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

/// <summary>
/// 物品锁定 / 解锁（第 309 节）。
///
/// 客户端本来就有这个功能，绑定链是完整的：
///   uw_logistics_tips_item3:Construct -> BtnAddEvent(self.BtnLock, function() self:SetLock() end)
///   uw_logistics_tips_item3:SetLock   -> me:CallGS("Item_SetLock", {ItemId=..., Lock=not HasFlag(FLAG_LOCK)})
///   uw_logistics_tips_item3:UpdateState -> 按 HasFlag(Item.FLAG_LOCK) 切换 Locked / Unlocked 两个图示
///
/// 所以按钮不是「没绑定」，而是服务端根本没有 Item_SetLock 这个接口，
/// 请求打过来直接落在路由默认分支，没有回包也没有物品同步，
/// 客户端既不会改图标也不会改数据 —— 看起来就是「点了没反应 / 灰的」。
///
/// 这里按客户端约定补齐：改 FLAG_LOCK 位，落库，回 Item_SetLock + NtfSyncPlayer.Items，
/// 客户端拿到更新后的 Item.Flag 就能把图标切过去。
/// </summary>
[CallGSApi("Item_SetLock")]
public class Item_SetLock : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ItemSetLockParam req;
		try
		{
			req = JsonSerializer.Deserialize<ItemSetLockParam>(param);
		}
		catch
		{
			req = null;
		}
		if (req == null || req.ItemId <= 0)
		{
			await CallGSRouter.SendScript(connection, "Item_SetLock", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint uid = (uint)req.ItemId;
		BaseGameItemInfo item = player.InventoryManager.GetNormalItem(uid);
		if (item == null)
		{
			item = (BaseGameItemInfo)player.InventoryManager.GetWeaponItem(uid);
		}
		if (item == null)
		{
			item = (BaseGameItemInfo)player.InventoryManager.GetSupportCardItem(uid);
		}
		if (item == null)
		{
			await CallGSRouter.SendScript(connection, "Item_SetLock", "{\"sErr\":\"error.ItemNotExists\"}");
			return;
		}
		bool hasLock = (item.Flag & ItemFlagEnum.FLAG_LOCK) != 0;
		if (hasLock != req.Lock)
		{
			item.Flag = (req.Lock ? (item.Flag | ItemFlagEnum.FLAG_LOCK) : (item.Flag & ~ItemFlagEnum.FLAG_LOCK));
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			Logger.GetByClassName().Info($"[ItemLock] uid={player.Uid} item={uid} tid={item.TemplateId} type={item.ItemType} lock={req.Lock}");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.Add(item.ToProto());
		await CallGSRouter.SendScript(connection, "Item_SetLock", "{}", sync);
	}
}
