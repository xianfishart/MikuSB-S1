using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

/// <summary>
/// 后勤（SupporterCard）升级。
///
/// 第 305 节：原来这里只在 InventoryData.Items 里找材料，但客户端把「另一张后勤卡」
/// 也当作升级材料发过来（Id = 卡的 UniqueId）。后勤卡存在 InventoryData.SupportCards 里、
/// 不在 Items 里，所以整条请求一定会走进「材料不存在」的早退分支：
/// 不发经验、也不消耗材料，客户端表现为「点了升级没反应 / 不加经验」。
/// 现在两处都查：普通物品按数量扣，后勤卡整张吃掉并给对应 ProvideExp。
/// </summary>
[CallGSApi("SupporterCard_Upgrade")]
public class SupporterCard_Upgrade : ICallGSHandler
{
	private static readonly Logger Log = new Logger("SupportUpgrade");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SupporterCardUpgradeParam req = JsonSerializer.Deserialize<SupporterCardUpgradeParam>(param);
		if (req == null || req.SupportCardUid == 0 || req.Materials == null || req.Materials.Count == 0)
		{
			Log.Warn($"reject(param) uid={connection.Player.Uid} req={param}");
			await SendEmpty(connection);
			return;
		}
		GameSupportCardInfo supportCard = player.InventoryManager.GetSupportCardItem((uint)req.SupportCardUid);
		if (supportCard == null)
		{
			Log.Warn($"reject(notfound) uid={connection.Player.Uid} card={req.SupportCardUid}");
			await SendEmpty(connection);
			return;
		}
		SupportCardExcel supportCardExcel = FindExcel(supportCard.TemplateId);
		uint maxLevel = supportCardExcel?.MaxLevel ?? 10;

		List<(BaseGameItemInfo Item, uint Num, uint Exp)> itemMats = new List<(BaseGameItemInfo, uint, uint)>();
		List<(GameSupportCardInfo Card, uint Exp)> cardMats = new List<(GameSupportCardInfo, uint)>();
		HashSet<uint> seen = new HashSet<uint>();
		uint gainedExp = 0u;

		foreach (UpgradeMaterial mat in req.Materials)
		{
			if (mat == null || mat.Id == 0 || mat.Num == 0)
			{
				Log.Warn($"reject(mat/param) uid={connection.Player.Uid} mat={mat?.Id} num={mat?.Num}");
				await SendEmpty(connection);
				return;
			}
			uint matId = (uint)mat.Id;
			if (!seen.Add(matId))
			{
				continue;
			}
			BaseGameItemInfo item = player.InventoryManager.GetNormalItem(matId);
			if (item != null)
			{
				if (item.ItemCount < mat.Num)
				{
					Log.Warn($"reject(item/count) uid={connection.Player.Uid} mat={matId} have={item.ItemCount} need={mat.Num}");
					await SendEmpty(connection);
					return;
				}
				uint provideExp = ResolveItemExp(item);
				Log.Info($"mat item uid={item.UniqueId} tid={item.TemplateId} num={mat.Num} exp={provideExp}");
				if (provideExp == 0)
				{
					Log.Warn($"reject(item/noexp) uid={connection.Player.Uid} mat={matId} tid={item.TemplateId}");
					await SendEmpty(connection);
					return;
				}
				itemMats.Add((item, mat.Num, provideExp));
				gainedExp += provideExp * mat.Num;
				continue;
			}
			GameSupportCardInfo matCard = player.InventoryManager.GetSupportCardItem(matId);
			if (matCard != null)
			{
				if (matCard.UniqueId == supportCard.UniqueId)
				{
					Log.Warn($"reject(mat/self) uid={connection.Player.Uid} card={supportCard.UniqueId}");
					await SendEmpty(connection);
					return;
				}
				// 第 308 节：锁定的卡不能被当材料吃掉。
				if ((matCard.Flag & ItemFlagEnum.FLAG_LOCK) != 0)
				{
					Log.Warn($"reject(mat/locked) uid={connection.Player.Uid} card={matCard.UniqueId}");
					await CallGSRouter.SendScript(connection, "Logistics_Upgrade", "{\"sErr\":\"ui.ItemLock\"}");
					return;
				}
				// 客户端的经验预览（umg_logistics_up.CanAddItem / GetCostExp）对后勤卡材料算的是
				//   num * ProvideExp + Item.GetExpAndSilverNum(卡) + 卡.Exp
				// 其中 GetExpAndSilverNum 对后勤卡直接返回 0，所以客户端口径是
				//   ProvideExp * Num + 卡当前等级内的剩余经验
				//
				// 第 307 节：这个口径少了「这张卡自己升级时已经吃进去的经验」。
				// 满级卡的 Exp 在升级循环里会被清零，于是喂一张 15/16 级满级卡只算卡面值
				// （金卡 9000），等于把攒了十几级的经验白白吞掉，玩家表现为「满级卡喂养不加经验」。
				// 现在按「等级继承」折算：卡面值 + Σ 该卡从 1 级升到当前级所需的经验 + 当前等级内的经验，
				// 也就是把这张卡身上累计投入的经验原样转移给被升级的卡。
				uint cardBase = FindExcel(matCard.TemplateId)?.ProvideExp ?? 0u;
				uint carried = AccumulatedExp(matCard);
				uint provideExp2 = cardBase + carried;
				Log.Info($"mat card uid={matCard.UniqueId} tid={matCard.TemplateId} lv={matCard.Level} " +
					$"exp={provideExp2}(base={cardBase}+carried={carried}, cur={matCard.Exp})");
				if (provideExp2 == 0)
				{
					Log.Warn($"reject(card/noexp) uid={connection.Player.Uid} mat={matId} tid={matCard.TemplateId}");
					await SendEmpty(connection);
					return;
				}
				cardMats.Add((matCard, provideExp2));
				gainedExp += provideExp2;
				continue;
			}
			Log.Warn($"reject(mat/notfound) uid={connection.Player.Uid} mat={matId} num={mat.Num}");
			await SendEmpty(connection);
			return;
		}

		List<Item> syncItems = new List<Item>();
		foreach (var (item2, num, _) in itemMats)
		{
			item2.ItemCount = (num >= item2.ItemCount) ? 0u : (item2.ItemCount - num);
			Item proto = item2.ToProto();
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				proto.Count = 0u;
			}
			syncItems.Add(proto);
		}
		foreach (var (matCard2, _) in cardMats)
		{
			Item proto2 = matCard2.ToProto();
			proto2.Count = 0u;
			syncItems.Add(proto2);
			player.InventoryManager.InventoryData.SupportCards.Remove(matCard2.UniqueId);
		}

		uint oldLevel = supportCard.Level;
		uint oldExp = supportCard.Exp;
		if (supportCard.Level == 0)
		{
			supportCard.Level = 1u;
		}
		supportCard.Exp += gainedExp;
		while (supportCard.Level < maxLevel)
		{
			uint expNeeded = GetExpNeeded(supportCard.Level);
			if (expNeeded == 0 || supportCard.Exp < expNeeded)
			{
				break;
			}
			supportCard.Exp -= expNeeded;
			supportCard.Level++;
		}
		if (supportCard.Level >= maxLevel)
		{
			supportCard.Exp = 0u;
			supportCard.Level = maxLevel;
			if (supportCardExcel != null)
			{
				int currentSlots = Enumerable.Range(1, 3).Count((int slot) => SupportAffixStateService.HasAffix(supportCard, slot));
				int totalSlots = supportCardExcel.TotalAffixCount;
				if (currentSlots < totalSlots && currentSlots < supportCardExcel.AffixPool.Count)
				{
					int poolId = supportCardExcel.AffixPool[currentSlots];
					var (affixId, tier) = SupportAffixService.GenerateRandomAffix(poolId);
					if (affixId != 0)
					{
						SupportAffixStateService.SetAffix(supportCard, currentSlots + 1, affixId, tier);
					}
				}
			}
		}
		syncItems.Add(supportCard.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		TaskEventService.OnSupportUpgrade(player);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		SupportRikiService.Sync(player, sync);
		// 第 438 节：活动任务「团队协作 = 将任意后勤升级 1 次」（Type 44）。
		if (supportCard.Level > oldLevel)
		{
			MikuSB.GameServer.Game.Activity.ActivityQuestService.Add(player, 44, 1, sync);
		}
		Log.Info($"ok uid={connection.Player.Uid} card={supportCard.UniqueId} items={itemMats.Count} cards={cardMats.Count} exp+={gainedExp} lv {oldLevel}({oldExp}) -> {supportCard.Level}({supportCard.Exp}) max={maxLevel}");
		await CallGSRouter.SendScript(connection, "Logistics_Upgrade", "{}", sync);
	}

	private static SupportCardExcel FindExcel(ulong templateId)
	{
		return GameData.SupportCardData.FirstOrDefault((SupportCardExcel x) => x.TemplateId == templateId);
	}

	/// <summary>
	/// 一张后勤卡身上"累计投入"的经验：从 1 级升到当前级所需的全部经验 + 当前等级内未用完的 Exp。
	/// 把它当材料吃掉时按这个数值折算，等价于「等级继承」——满级卡喂下去，被喂的卡直接顶到上限。
	/// 满级时升级循环会把 Exp 清零，所以光看卡.Exp 会得到 0，必须把等级那部分补回来。
	/// </summary>
	private static uint AccumulatedExp(GameSupportCardInfo card)
	{
		uint total = card.Exp;
		uint level = (card.Level < 1) ? 1u : card.Level;
		for (uint lv = 1; lv < level; lv++)
		{
			uint need = GetExpNeeded(lv);
			// 曲线末尾的 999999999 是封顶哨兵，遇到就停，避免把经验算爆。
			if (need == 0 || need >= 999999999u)
			{
				break;
			}
			total += need;
		}
		return total;
	}

	/// <summary>
	/// 计算一个普通物品能给多少后勤经验。
	///
	/// 注意 SuppliesData 的键是 (uint)FromGdpl(...)，只保留了 Genre | Detail&lt;&lt;16，
	/// Particular / Level 被截断 —— 同一 G/D 下的 4 个等级会互相覆盖（last wins），
	/// 例如 (5,1,1,1) 的 600 会被 (5,1,1,4) 的 9000 顶掉，直接查字典会放大 15 倍。
	/// 所以这里先用完整的 G/D/P/L 全表精确匹配，匹配不到才退回字典。
	/// </summary>
	private static uint ResolveItemExp(BaseGameItemInfo item)
	{
		foreach (SuppliesExcel all in GameData.AllSuppliesData)
		{
			if (GameResourceTemplateId.FromGdpl(all.Genre, all.Detail, all.Particular, all.Level) == item.TemplateId)
			{
				return all.ProvideExp;
			}
		}
		if (GameData.SuppliesData.TryGetValue((uint)item.TemplateId, out SuppliesExcel sup))
		{
			return sup.ProvideExp;
		}
		return 0u;
	}

	private static uint GetExpNeeded(uint level)
	{
		if (GameData.UpgradeExpData.TryGetValue((int)level, out UpgradeExpExcel value))
		{
			return value.SusNeedExp;
		}
		return 0u;
	}

	private static Task SendEmpty(Connection connection)
	{
		return CallGSRouter.SendScript(connection, "Logistics_Upgrade", "{}");
	}
}
