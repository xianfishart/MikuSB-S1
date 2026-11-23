using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

[CallGSApi("FishingServer_ConvertFood")]
public class FishingServer_ConvertFood : ICallGSHandler
{
	private const uint FishingGroupId = 32u;

	private const uint CashGroupId = 1u;

	private const uint FoodBaseSid = 30000u;

	private const uint FoodAvaTimeSubType = 1u;

	private const uint ExploreAvaTimeSubType = 2u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		FishingConvertFoodParam req = JsonSerializer.Deserialize<FishingConvertFoodParam>(param);
		if (req == null || req.FoodId <= 0 || req.Num == 0)
		{
			await CallGSRouter.SendScript(connection, "FishingServer_ConvertFood", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.FishingFoodData.TryGetValue((uint)req.FoodId, out FishingFoodExcel food))
		{
			await CallGSRouter.SendScript(connection, "FishingServer_ConvertFood", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		uint count = Math.Max(1u, req.Num);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!HasEnoughMaterials(player.InventoryManager.InventoryData, food.NeedItem, count) || !HasEnoughCash(player.Data, food.BaitNum, count))
		{
			await CallGSRouter.SendScript(connection, "FishingServer_ConvertFood", "{\"sError\":\"tip.girlcard_cmd_err\"}");
			return;
		}
		ConsumeMaterials(player.InventoryManager.InventoryData, food.NeedItem, count, sync.Items);
		ConsumeCash(player, food.BaitNum, count, sync);
		JsonObject response = new JsonObject { ["nFoodID"] = req.FoodId };
		switch (food.FoodType)
		{
		case 1u:
			ApplyFoodDuration(player, food, 1u, count, sync);
			break;
		case 2u:
			response["tbBait"] = await CreateItemsAsync(player, sync, food.CreateItems, count);
			break;
		case 3u:
			ApplyFoodDuration(player, food, 2u, count, sync);
			break;
		default:
			await CallGSRouter.SendScript(connection, "FishingServer_ConvertFood", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "FishingServer_ConvertFood", response.ToJsonString(), sync);
	}

	private static bool HasEnoughMaterials(InventoryData inventory, IEnumerable<List<uint>> costs, uint multiplier)
	{
		foreach (List<uint> cost in costs)
		{
			if (cost.Count < 5)
			{
				return false;
			}
			ulong templateId = GameResourceTemplateId.FromGdpl(cost[0], cost[1], cost[2], cost[3]);
			BaseGameItemInfo baseGameItemInfo = inventory.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
			uint num = checked(cost[4] * multiplier);
			if (baseGameItemInfo == null || baseGameItemInfo.ItemCount < num)
			{
				return false;
			}
		}
		return true;
	}

	private static void ConsumeMaterials(InventoryData inventory, IEnumerable<List<uint>> costs, uint multiplier, ICollection<Item> syncItems)
	{
		foreach (List<uint> cost in costs)
		{
			ulong templateId = GameResourceTemplateId.FromGdpl(cost[0], cost[1], cost[2], cost[3]);
			BaseGameItemInfo baseGameItemInfo = inventory.Items.Values.First((BaseGameItemInfo x) => x.TemplateId == templateId);
			uint num = checked(cost[4] * multiplier);
			baseGameItemInfo.ItemCount -= num;
			if (baseGameItemInfo.ItemCount == 0)
			{
				inventory.Items.Remove(baseGameItemInfo.UniqueId);
				Item item = baseGameItemInfo.ToProto();
				item.Count = 0u;
				syncItems.Add(item);
			}
			else
			{
				syncItems.Add(baseGameItemInfo.ToProto());
			}
		}
	}

	private static bool HasEnoughCash(PlayerGameData data, IReadOnlyList<uint> baitNum, uint multiplier)
	{
		if (baitNum.Count < 2)
		{
			return true;
		}
		uint num = baitNum[0];
		uint num2 = checked(baitNum[1] * multiplier);
		uint sid = num * 2 + 1;
		return (data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == sid)?.Val ?? 0) >= num2;
	}

	private static void ConsumeCash(PlayerInstance player, IReadOnlyList<uint> baitNum, uint multiplier, NtfSyncPlayer sync)
	{
		if (baitNum.Count >= 2)
		{
			uint num = baitNum[0];
			uint sid = num * 2 + 1;
			uint num2 = checked(baitNum[1] * multiplier);
			PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 1u, sid);
			orCreateAttr.Val -= num2;
			SyncAttr(player, sync, orCreateAttr);
		}
	}

	private static void ApplyFoodDuration(PlayerInstance player, FishingFoodExcel food, uint subType, uint count, NtfSyncPlayer sync)
	{
		uint sid = 30000 + food.Id * 10 + subType;
		PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, 32u, sid);
		uint val = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		uint num = Math.Max(orCreateAttr.Val, val);
		orCreateAttr.Val = checked(num + food.EffectTime * count);
		SyncAttr(player, sync, orCreateAttr);
	}

	private static async Task<JsonArray> CreateItemsAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> createItem, uint multiplier)
	{
		JsonArray rewards = new JsonArray();
		if (createItem.Count < 5)
		{
			return rewards;
		}
		ItemTypeEnum itemType = (ItemTypeEnum)createItem[0];
		uint detail = createItem[1];
		uint particular = createItem[2];
		uint level = createItem[3];
		uint totalCount = checked(createItem[4] * multiplier);
		switch (itemType)
		{
		case ItemTypeEnum.TYPE_SUPPLIES:
		{
			uint templateId = (uint)GameResourceTemplateId.FromGdpl(createItem[0], detail, particular, level);
			if (GameData.SuppliesData.TryGetValue(templateId, out SuppliesExcel supplies))
			{
				BaseGameItemInfo item2 = await player.InventoryManager.AddSuppliesItem(supplies, totalCount, sendPacket: false);
				if (item2 != null)
				{
					sync.Items.Add(item2.ToProto());
				}
			}
			break;
		}
		case ItemTypeEnum.TYPE_USEABLE:
		{
			BaseGameItemInfo item = AddOtherItem(player.InventoryManager.InventoryData, detail, particular, level, totalCount);
			if (item != null)
			{
				sync.Items.Add(item.ToProto());
			}
			break;
		}
		}
		InlineArray5<JsonNode> buffer = default;
		buffer[0] = (int)createItem[0];
		buffer[1] = (int)detail;
		buffer[2] = (int)particular;
		buffer[3] = (int)level;
		buffer[4] = (int)totalCount;
		rewards.Add(new JsonArray(buffer));
		return rewards;
	}

	private static BaseGameItemInfo? AddOtherItem(InventoryData inventory, uint detail, uint particular, uint level, uint count)
	{
		// 2026-11-15（修复记录第 347 节）：这里原本是
		//     uint templateId = (uint)GameResourceTemplateId.FromGdpl(4u, detail, particular, level);
		// FromGdpl 返回 ulong，强转 uint 会把高 32 位（Particular / Level）整段砍掉，
		// 于是任何 [4, 1, x, y] 的食材兑换出来都会变成 TemplateId 对应坐标 4-1-0-0：
		// 客户端查不到该模板 → 无名无图标、点「使用」卡加载，背包里出现一条幽灵道具。
		// 必须全程用 ulong。
		ulong templateId = GameResourceTemplateId.FromGdpl(4u, detail, particular, level);
		if (!GameData.OtherItemByFullId.TryGetValue(templateId, out OtherItemExcel value))
		{
			value = GameData.OtherItemData.Values.FirstOrDefault((OtherItemExcel x) => x.Genre == 4 && x.Detail == detail && x.Particular == particular && x.Level == level);
			if (value == null)
			{
				return null;
			}
		}
		uint val = ((value.GMnum != 0) ? value.GMnum : 99999u);
		BaseGameItemInfo baseGameItemInfo = inventory.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
		if (baseGameItemInfo != null)
		{
			baseGameItemInfo.ItemCount = Math.Min(baseGameItemInfo.ItemCount + count, val);
			return baseGameItemInfo;
		}
		BaseGameItemInfo baseGameItemInfo2 = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = inventory.NextUniqueUid++,
			ItemType = ItemTypeEnum.TYPE_USEABLE,
			ItemCount = Math.Min(count, val)
		};
		inventory.Items[baseGameItemInfo2.UniqueId] = baseGameItemInfo2;
		return baseGameItemInfo2;
	}

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid,
			Val = 0u
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
