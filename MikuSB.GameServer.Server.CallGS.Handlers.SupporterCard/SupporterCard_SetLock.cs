using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

/// <summary>
/// 后勤卡锁定 / 解锁（第 308 节）。
///
/// 原版客户端在 uw_logistics_tips_item3 的 PanelLock 下有 BtnLock，
/// 但点击没有任何回调（OnClicked 里没有绑定函数），点了发不出请求，
/// 表现就是「锁定按钮点不动」。服务端原本也没有对应接口。
/// 这里补一个接口，用物品标志位 FLAG_LOCK 记录锁定状态；
/// 锁定后：不能作为升级材料（SupporterCard_Upgrade 里拦截）、不能分解（Item_Recycle 里拦截）。
/// </summary>
[CallGSApi("SupporterCard_SetLock")]
public class SupporterCard_SetLock : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SupporterCardLockParam req = JsonSerializer.Deserialize<SupporterCardLockParam>(param);
		if (req == null || req.SupportCardUid == 0)
		{
			await CallGSRouter.SendScript(connection, "Logistics_SetLock", "{}");
			return;
		}
		GameSupportCardInfo card = player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "Logistics_SetLock", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		bool wantLock = req.Lock != 0;
		bool hasLock = (card.Flag & ItemFlagEnum.FLAG_LOCK) != 0;
		if (wantLock != hasLock)
		{
			card.Flag = (wantLock ? (card.Flag | ItemFlagEnum.FLAG_LOCK) : (card.Flag & ~ItemFlagEnum.FLAG_LOCK));
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.Add(card.ToProto());
		SupportRikiService.Sync(player, sync);
		await CallGSRouter.SendScript(connection, "Logistics_SetLock", "{}", sync);
	}
}
