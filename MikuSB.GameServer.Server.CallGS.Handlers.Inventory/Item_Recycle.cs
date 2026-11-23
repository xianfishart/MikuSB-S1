using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

[CallGSApi("Item_Recycle")]
public class Item_Recycle : ICallGSHandler
{
	/// <summary>新星开拓伙伴（怪物卡）的技能槽：4..6，每槽是 [技能Id, 等级]。</summary>
	private const int FirstSkillSlot = 4;

	private const int LastSkillSlot = 6;

	/// <summary>skilllist.json 里 25 个技能的 CostId 都是 1。</summary>
	private const int SkillCostId = 1;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ItemRecycleParam req = JsonSerializer.Deserialize<ItemRecycleParam>(param);
		if (req?.TbItems == null || req.TbItems.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Item_Recycle", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		RecycleConfig config = RecycleConfig.Load();
		List<(BaseGameItemInfo Item, int RecycleId)> itemsToRecycle = new List<(BaseGameItemInfo, int)>();
		List<MonsterCardItemInfo> cardsToRecycle = new List<MonsterCardItemInfo>();
		foreach (int uniqueId in req.TbItems)
		{
			BaseGameItemInfo item = FindItem(player, (uint)uniqueId);
			if (item == null)
			{
				await CallGSRouter.SendScript(connection, "Item_Recycle", "{\"sErr\":\"error.Recycle.ItemNotExists\"}");
				return;
			}
			// 第 308 节：锁定的物品不允许分解。
			if ((item.Flag & ItemFlagEnum.FLAG_LOCK) != 0)
			{
				await CallGSRouter.SendScript(connection, "Item_Recycle", "{\"sErr\":\"ui.ItemLock\"}");
				return;
			}
			// 第 342 节：新星开拓「解雇伙伴」走的就是这个接口（编队界面 SellOut），
			// 返还的是技能证书（按 skillcost.json 的 Recycle1..6 逐技能结算），
			// 不是武器/后勤那套 RecycleExcel，所以单独一条分支。
			if (item.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD)
			{
				cardsToRecycle.Add(AsMonsterCard(player, item));
				continue;
			}
			int recycleId = GetRecycleId(item);
			if (recycleId <= 0 || !config.HasConfig(recycleId))
			{
				await CallGSRouter.SendScript(connection, "Item_Recycle", "{\"sErr\":\"error.Recycle.ItemCanNotRecycle\"}");
				return;
			}
			itemsToRecycle.Add((item, recycleId));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (var item3 in itemsToRecycle)
		{
			BaseGameItemInfo item2 = item3.Item;
			int recycleId2 = item3.RecycleId;
			List<IReadOnlyList<uint>> rewards = config.CalcRewards(item2, recycleId2);
			foreach (IReadOnlyList<uint> reward in rewards)
			{
				await GrantRewardAsync(player, sync, reward);
			}
			RemoveItem(player.InventoryManager.InventoryData, item2, sync);
		}
		foreach (MonsterCardItemInfo card in cardsToRecycle)
		{
			foreach (IReadOnlyList<uint> reward in CalcMonsterCardRewards(card))
			{
				await GrantRewardAsync(player, sync, reward);
			}
			RemoveMonsterCard(player, card, sync);
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		if (cardsToRecycle.Count > 0)
		{
			VirCaptureCardSkillStore.Save(player.Uid);
			SyncBagNum(player, sync);
			// 开拓目标：214「累计解雇伙伴」
			VirCaptureMissionProgress.Add(player, VirCaptureMissionProgress.KeyDismiss, cardsToRecycle.Count);
			VirCaptureMissionProgress.SyncAll(player, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		await CallGSRouter.SendScript(connection, "Item_Recycle", "{}", sync);
	}

	private static BaseGameItemInfo? FindItem(PlayerInstance player, uint uniqueId)
	{
		BaseGameItemInfo item = player.InventoryManager.GetWeaponItem(uniqueId);
		if (item == null)
		{
			item = player.InventoryManager.GetSupportCardItem(uniqueId);
		}
		if (item == null && player.InventoryManager.InventoryData.Items.TryGetValue(uniqueId, out BaseGameItemInfo generic))
		{
			item = generic;
		}
		return item;
	}

	private static MonsterCardItemInfo AsMonsterCard(PlayerInstance player, BaseGameItemInfo item)
	{
		if (item is MonsterCardItemInfo card)
		{
			card.EnsureAffixs();
			return card;
		}
		MonsterCardItemInfo converted = new MonsterCardItemInfo
		{
			UniqueId = item.UniqueId,
			TemplateId = item.TemplateId,
			ItemCount = item.ItemCount,
			ItemType = item.ItemType,
			Flag = item.Flag,
			Level = item.Level,
			Exp = item.Exp
		};
		converted.EnsureAffixs();
		player.InventoryManager.InventoryData.Items[item.UniqueId] = converted;
		return converted;
	}

	/// <summary>
	/// 与客户端 <c>ItemRecycle.GetMonsterRewards</c> 完全一致：
	/// 技能槽 4..6 逐个取 GetSkillConfig(id).nCostId → tbRecycle[等级]，把条目依次拼起来。
	/// 等级 1 的 Recycle1 是空串，所以刚招募的伙伴解雇不返还任何证书（原版就这样）。
	/// </summary>
	private static List<List<uint>> CalcMonsterCardRewards(MonsterCardItemInfo card)
	{
		List<List<uint>> rewards = new List<List<uint>>();
		for (int slot = FirstSkillSlot; slot <= LastSkillSlot; slot++)
		{
			uint skillId = card.GetSlotId(slot);
			uint level = card.GetSlotLevel(slot);
			if (skillId == 0u || level == 0u)
			{
				continue;
			}
			foreach (List<uint> row in VirCaptureSkillCostConfig.Instance.GetRecycle(SkillCostId, level))
			{
				rewards.Add(row);
			}
		}
		return rewards;
	}

	private static void RemoveMonsterCard(PlayerInstance player, MonsterCardItemInfo card, NtfSyncPlayer sync)
	{
		Item proto = card.ToProto();
		proto.Count = 0u;
		sync.Items.Add(proto);
		player.InventoryManager.InventoryData.Items.Remove(card.UniqueId);
		VirCaptureCardSkillStore.Remove(player.Uid, card.UniqueId);
	}

	/// <summary>背包里的伙伴数量（128/5），HUD 上那个 N/上限 就是它。</summary>
	private static void SyncBagNum(PlayerInstance player, NtfSyncPlayer sync)
	{
		uint count = (uint)player.InventoryManager.InventoryData.Items.Values.Count((BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_MONSTER_CARD);
		VirCaptureStateHelper.SetUnsignedAttr(player, 5u, count, sync);
	}

	private static int GetRecycleId(BaseGameItemInfo item)
	{
		if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
		{
			return GameData.WeaponData.Values.FirstOrDefault((WeaponExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == item.TemplateId)?.RecycleID ?? 0;
		}
		if (item.ItemType == ItemTypeEnum.TYPE_SUPPORT)
		{
			return GameData.SupportCardData.FirstOrDefault((SupportCardExcel x) => x.TemplateId == item.TemplateId)?.RecycleID ?? 0;
		}
		return 0;
	}

	private static void RemoveItem(InventoryData inventory, BaseGameItemInfo item, NtfSyncPlayer sync)
	{
		Item item2 = item.ToProto();
		item2.Count = 0u;
		sync.Items.Add(item2);
		if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
		{
			inventory.Weapons.Remove(item.UniqueId);
		}
		else
		{
			inventory.SupportCards.Remove(item.UniqueId);
		}
	}

	private static async Task GrantRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
	{
		await player.RewardManager.GrantRewardAsync(reward, sync);
	}
}
