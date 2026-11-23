using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("IBLogic_BuyGoods")]
public class IBLogic_BuyGoods : ICallGSHandler
{
	private sealed class BoxRewardRow
	{
		public string ObtainType { get; set; } = "";

		public List<uint> Gdpln { get; set; } = new List<uint>();
	}

	private const uint BuyGroupId = 26u;

	private const uint RedGroupId = 113u;

	private const uint CashGroupId = 1u;

	private const uint BattlePassGroupId = 25u;

	private const uint BattlePassCurIdSid = 1u;

	private const uint BattlePassStatusSid = 2u;

	private const uint FirstRechargeGiftGid = 200u;

	private const uint FirstRechargeGiftSid = 14u;

	private static Dictionary<uint, List<BoxRewardRow>>? _boxRows;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		IbBuyGoodsParam req = JsonSerializer.Deserialize<IbBuyGoodsParam>(param);
		PlayerInstance player = connection.Player;
		if (req != null && req.Type == 3 && req.GoodsId != 0 && req.Count != 0)
		{
			await HandleBattlePassPurchase(connection, player, req);
			return;
		}
		if (req == null || req.GoodsId == 0 || req.Count == 0 || !GameData.IbGoodsData.TryGetValue(req.GoodsId, out IbGoodsExcel goods))
		{
			await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (goods.Type == 2)
		{
			await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		if (goods.LimitTimes != 0)
		{
			PlayerAttr buyAttr = GetOrCreateAttr(player, 26u, req.GoodsId);
			if (buyAttr.Val >= goods.LimitTimes)
			{
				await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", "{\"sErr\":\"tip.Mall_Limit_Buy\"}");
				return;
			}
		}
		List<List<uint>> rewardItems = BuildRewardItems(goods, req);
		if (rewardItems.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<uint> costEntry = ((req.Index == 2) ? goods.Cost2 : goods.Cost);
		try
		{
			File.AppendAllText("D:\\DSH\\.tools\\ib_buy.log", $"{DateTime.Now:HH:mm:ss} goods={req.GoodsId} idx={req.Index} count={req.Count} cost2={goods.Cost2.Count} cost1={goods.Cost.Count} entry={string.Join(',', costEntry)}\n");
		}
		catch
		{
		}
		if (costEntry.Count >= 2 && costEntry[0] != 0 && costEntry[0] < 100)
		{
			uint sid = ((costEntry[0] == 1) ? 3u : (costEntry[0] * 2 + 1));
			uint amount = costEntry[1] * Math.Max(1u, req.Count);
			PlayerAttr costAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == sid);
			try
			{
				File.AppendAllText("D:\\DSH\\.tools\\ib_buy.log", $"  sid={sid} need={amount} before={costAttr?.Val} found={costAttr != null}\n");
			}
			catch
			{
			}
			if (costAttr == null || costAttr.Val < amount)
			{
				await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", "{\"sErr\":\"tip.cash_not_enough\"}");
				return;
			}
			costAttr.Val -= amount;
			SyncAttr(player, sync, costAttr);
			if (costEntry[0] == 1)
			{
				foreach (var (key, value) in player.BuildMoneySync())
				{
					sync.Money[key] = value;
				}
			}
			try
			{
				File.AppendAllText("D:\\DSH\\.tools\\ib_buy.log", $"  after={costAttr.Val}\n");
			}
			catch
			{
			}
		}
		foreach (List<uint> reward in rewardItems)
		{
			await GrantRewardAsync(player, sync, reward);
		}
		if (IsBitGoldPackage(goods))
		{
			List<uint> chargeCost = ((req.Index == 2) ? goods.Cost2 : goods.Cost);
			if (chargeCost.Count >= 2 && chargeCost[1] != 0)
			{
				PlayerAttr chargeAttr = GetOrCreateAttr(player, 0u, 18u);
				chargeAttr.Val += chargeCost[1] * req.Count;
				SyncAttr(player, sync, chargeAttr);
			}
			PlayerAttr giftAttr = GetOrCreateAttr(player, 200u, 14u);
			if (giftAttr.Val == 0)
			{
				giftAttr.Val = 1u;
				SyncAttr(player, sync, giftAttr);
				foreach (List<uint> reward2 in LoadFirstRechargeRewards())
				{
					await GrantRewardAsync(player, sync, reward2);
					rewardItems.Add(reward2);
				}
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		PlayerAttr buyCountAttr = GetOrCreateAttr(player, 26u, req.GoodsId);
		buyCountAttr.Val += req.Count;
		SyncAttr(player, sync, buyCountAttr);
		PlayerAttr redAttr = GetOrCreateAttr(player, 113u, req.GoodsId);
		if (redAttr.Val == 0)
		{
			redAttr.Val = 1u;
			SyncAttr(player, sync, redAttr);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonArray responseGoods = new JsonArray();
		foreach (List<uint> reward3 in rewardItems)
		{
			JsonArray row = new JsonArray();
			foreach (uint value2 in reward3)
			{
				row.Add((int)value2);
			}
			responseGoods.Add(row);
		}
		JsonObject rsp = new JsonObject
		{
			["nGoodsId"] = (int)req.GoodsId,
			["tbGoods"] = responseGoods
		};
		string productId = goods.GetProductId();
		if (!string.IsNullOrWhiteSpace(productId))
		{
			rsp["sProductId"] = productId;
		}
		List<uint> cost = ((req.Index == 2) ? goods.Cost2 : goods.Cost);
		if (cost.Count >= 2)
		{
			rsp["nTotalPrice"] = (int)cost[1];
		}
		await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", rsp.ToJsonString(), sync);
	}

	private static async Task HandleBattlePassPurchase(Connection connection, PlayerInstance player, IbBuyGoodsParam req)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint battlePassId = ResolveCurrentBattlePassId();
		if (battlePassId != 0)
		{
			PlayerAttr curIdAttr = GetOrCreateAttr(player, 25u, 1u);
			curIdAttr.Val = battlePassId;
			SyncAttr(player, sync, curIdAttr);
		}
		PlayerAttr statusAttr = GetOrCreateAttr(player, 25u, 2u);
		if (statusAttr.Val < 2)
		{
			statusAttr.Val = 2u;
			SyncAttr(player, sync, statusAttr);
		}
		PlayerAttr buyCountAttr = GetOrCreateAttr(player, 26u, req.GoodsId);
		buyCountAttr.Val += req.Count;
		SyncAttr(player, sync, buyCountAttr);
		PlayerAttr redAttr = GetOrCreateAttr(player, 113u, req.GoodsId);
		if (redAttr.Val == 0)
		{
			redAttr.Val = 1u;
			SyncAttr(player, sync, redAttr);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		JsonObject rsp = new JsonObject
		{
			["nGoodsId"] = (int)req.GoodsId,
			["tbGoods"] = new JsonArray()
		};
		await CallGSRouter.SendScript(connection, "IBLogic_BuyGoods", rsp.ToJsonString(), sync);
	}

	private static bool IsBitGoldPackage(IbGoodsExcel goods)
	{
		if (goods.Type != 1)
		{
			return false;
		}
		string productId = goods.GetProductId();
		if (string.IsNullOrWhiteSpace(productId) || !productId.Contains("currency", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return goods.Item.Count >= 4 && goods.Item[0] == 4;
	}

	private static List<List<uint>> LoadFirstRechargeRewards()
	{
		List<List<uint>> list = new List<List<uint>>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "activity", "activities.json");
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return list;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject))
				{
					continue;
				}
				JsonNode? jsonNode = jsonObject["Id"];
				if (jsonNode == null || jsonNode.GetValue<int>() != 14 || !(jsonObject["CustomData"] is JsonArray jsonArray2))
				{
					continue;
				}
				foreach (JsonNode item2 in jsonArray2)
				{
					if (item2 is JsonArray source)
					{
						List<uint> list2 = source.Select((JsonNode x) => x?.GetValue<uint>() ?? 0).ToList();
						if (list2.Count >= 5)
						{
							list.Add(list2);
						}
					}
				}
				break;
			}
		}
		catch
		{
		}
		return list;
	}

	private static List<List<uint>> BuildRewardItems(IbGoodsExcel goods, IbBuyGoodsParam req)
	{
		List<List<uint>> list = new List<List<uint>>();
		List<List<uint>> list2 = TryExpandOutfitBundle(goods);
		if (list2 != null)
		{
			foreach (List<uint> item in list2)
			{
				list.Add(WithCount(item, req.Count));
			}
			return list;
		}
		if (goods.Item.Count >= 4 && goods.Item.Take(4).All((uint x) => x != 0))
		{
			list.Add(WithCount(goods.Item, req.Count));
		}
		List<uint>? selectItem = req.SelectItem1;
		if (selectItem != null && selectItem.Count >= 4 && req.SelectItem1.Take(4).All((uint x) => x != 0))
		{
			list.Add(WithCount(req.SelectItem1, req.Count));
		}
		List<uint>? selectItem2 = req.SelectItem2;
		if (selectItem2 != null && selectItem2.Count >= 4 && req.SelectItem2.Take(4).All((uint x) => x != 0))
		{
			list.Add(WithCount(req.SelectItem2, req.Count));
		}
		return list;
	}

	private static List<List<uint>>? TryExpandOutfitBundle(IbGoodsExcel goods)
	{
		if (goods.Type != 5 || goods.Item.Count < 4)
		{
			return null;
		}
		ulong key = GameResourceTemplateId.FromGdpl(goods.Item[0], goods.Item[1], goods.Item[2], goods.Item[3]);
		if (!GameData.OtherItemByFullId.TryGetValue(key, out OtherItemExcel value) || value.LuaType != "itembox")
		{
			return null;
		}
		if (goods.Type != 5 && value.Icon != 0)
		{
			return null;
		}
		List<BoxRewardRow> list = LoadBoxRows(value.Param1);
		if (list.Count == 0 || list.Any((BoxRewardRow x) => string.Equals(x.ObtainType, "Select", StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		return list.Select((BoxRewardRow x) => x.Gdpln).ToList();
	}

	private static List<BoxRewardRow> LoadBoxRows(uint index)
	{
		if (_boxRows == null)
		{
			_boxRows = LoadAllBoxRows();
		}
		return _boxRows.GetValueOrDefault(index) ?? new List<BoxRewardRow>();
	}

	private static Dictionary<uint, List<BoxRewardRow>> LoadAllBoxRows()
	{
		Dictionary<uint, List<BoxRewardRow>> dictionary = new Dictionary<uint, List<BoxRewardRow>>();
		string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "item", "box.json");
		if (!File.Exists(path))
		{
			return dictionary;
		}
		JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
		if (jsonArray == null)
		{
			return dictionary;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonObject jsonObject))
			{
				continue;
			}
			uint num = jsonObject["Index"]?.GetValue<uint>() ?? 0;
			JsonArray jsonArray2 = jsonObject["GDPLN"] as JsonArray;
			if (num != 0 && jsonArray2 != null && jsonArray2.Count >= 5)
			{
				if (!dictionary.TryGetValue(num, out var value))
				{
					value = (dictionary[num] = new List<BoxRewardRow>());
				}
				value.Add(new BoxRewardRow
				{
					ObtainType = (jsonObject["ObtainType"]?.GetValue<string>() ?? ""),
					Gdpln = jsonArray2.Select((JsonNode x) => x?.GetValue<uint>() ?? 0).ToList()
				});
			}
		}
		return dictionary;
	}

	private static List<uint> WithCount(IReadOnlyList<uint> item, uint buyCount)
	{
		List<uint> list = item.Take(5).ToList();
		while (list.Count < 5)
		{
			list.Add(1u);
		}
		list[4] = Math.Max(1u, list[4]) * Math.Max(1u, buyCount);
		return list;
	}

	internal static async Task GrantRewardAsync(PlayerInstance player, NtfSyncPlayer sync, IReadOnlyList<uint> reward)
	{
		await player.RewardManager.GrantRewardAsync(reward, sync);
	}

	private static BaseGameItemInfo? AddOtherItem(InventoryData inventory, uint genre, uint detail, uint particular, uint level, uint count)
	{
		ulong templateId = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
		if (!GameData.OtherItemByFullId.TryGetValue(templateId, out OtherItemExcel value))
		{
			value = GameData.OtherItemData.Values.FirstOrDefault((OtherItemExcel x) => x.Genre == genre && x.Detail == detail && x.Particular == particular && x.Level == level);
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

	private static bool TryGrantCashBox(PlayerInstance player, NtfSyncPlayer sync, uint detail, uint particular, uint level, uint count)
	{
		ulong key = GameResourceTemplateId.FromGdpl(4u, detail, particular, level);
		if (!GameData.OtherItemByFullId.TryGetValue(key, out OtherItemExcel value))
		{
			return false;
		}
		string luaType = value.LuaType;
		if (1 == 0)
		{
		}
		uint num = luaType switch
		{
			"money_box" => 1u, 
			"gold_box" => 2u, 
			"silver_box" => 3u, 
			"vigor_box" => 4u, 
			_ => 0u, 
		};
		if (1 == 0)
		{
		}
		uint num2 = num;
		if (num2 == 0 || value.Param1 == 0)
		{
			return false;
		}
		uint num3 = checked(value.Param1 * count);
		uint sid = num2 * 2 + 1;
		PlayerAttr orCreateAttr = GetOrCreateAttr(player, 1u, sid);
		orCreateAttr.Val += num3;
		SyncAttr(player, sync, orCreateAttr);
		if (num2 == 1)
		{
			foreach (var (key2, value2) in player.BuildMoneySync())
			{
				sync.Money[key2] = value2;
			}
		}
		return true;
	}

	private static uint ResolveCurrentBattlePassId()
	{
		DateTime now = DateTime.Now;
		var source = (from x in GameData.BattlePassTimeData.Values
			select new
			{
				Config = x,
				Start = ParseConfigTime(x.StartTime),
				End = ParseConfigTime(x.EndTime)
			} into x
			where x.Start.HasValue && x.End.HasValue
			orderby x.Start
			select x).ToList();
		return source.FirstOrDefault(x =>
		{
			int result;
			if (x.Start <= now)
			{
				DateTime value = now;
				DateTime? end = x.End;
				result = ((value < end) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		})?.Config.Id ?? source.LastOrDefault(x => x.Start <= now && x.End > x.Start)?.Config.Id ?? 0;
	}

	private static DateTime? ParseConfigTime(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = raw.Trim().Trim(new char[2] { '[', ']' });
		if (text.Length != 12)
		{
			return null;
		}
		DateTime result;
		return DateTime.TryParseExact(text, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) ? new DateTime?(result) : ((DateTime?)null);
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint gid, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
