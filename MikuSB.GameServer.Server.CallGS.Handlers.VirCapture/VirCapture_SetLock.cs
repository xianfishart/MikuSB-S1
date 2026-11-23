using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「伙伴编队 → 锁定/解锁」（第 342 节新增）。
///
/// 客户端 umg_vircapture_formation:DoClickLock
///   → VirCapture.DoLockAndUnlock(item)
///      {MonsterId = item:Id(), bLock = not item:HasFlag(Item.FLAG_LOCK)}
///      me:CallGS("VirCapture_SetLock", ...)
///
/// 锁定的伙伴不能解雇（Item_Recycle 里对 FLAG_LOCK 直接拒绝，第 308 节），
/// 所以这里只是把 Item.Flag 的 LOCK 位翻一下并同步回客户端。
/// </summary>
[CallGSApi("VirCapture_SetLock")]
public class VirCapture_SetLock : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		LockParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<LockParam>(param);
			}
		}
		catch (JsonException)
		{
		}
		if (req == null || req.MonsterId == 0u)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!player.InventoryManager.InventoryData.Items.TryGetValue(req.MonsterId, out BaseGameItemInfo item)
			|| item.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		item.Flag = (req.Lock
			? (item.Flag | ItemFlagEnum.FLAG_LOCK)
			: (item.Flag & ~ItemFlagEnum.FLAG_LOCK));
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.Add(item.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await CallGSRouter.SendScript(connection, "VirCapture_SetLock", "{}", sync);
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCapture_SetLock", arg);
	}

	private sealed class LockParam
	{
		[JsonPropertyName("MonsterId")]
		public uint MonsterId { get; set; }

		[JsonPropertyName("bLock")]
		public bool Lock { get; set; }
	}
}
