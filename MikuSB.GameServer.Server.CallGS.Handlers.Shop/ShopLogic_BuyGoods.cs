using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("ShopLogic_BuyGoods")]
public class ShopLogic_BuyGoods : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		int goodsId = 0;
		int count = 1;
		int buyMode = 1;
		try
		{
			JsonNode json = JsonNode.Parse(param);
			goodsId = (json?["nGoodsId"]?.GetValue<int>()).GetValueOrDefault();
			count = json?["nCount"]?.GetValue<int>() ?? 1;
			buyMode = json?["nBuyMode"]?.GetValue<int>() ?? 1;
		}
		catch
		{
		}
		if (goodsId <= 0 || count <= 0)
		{
			await CallGSRouter.SendScript(connection, "ShopLogic_BuyGoods", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		List<uint> gdpln = FindGdpln(goodsId);
		if (gdpln == null || gdpln.Count < 5)
		{
			await CallGSRouter.SendScript(connection, "ShopLogic_BuyGoods", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		List<uint> reward = new List<uint>(gdpln.Take(5));
		reward[4] = Math.Max(1u, reward[4]) * (uint)Math.Max(1, count);
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<uint> price = FindPrice(goodsId, buyMode);
		if (price != null && price.Count >= 2)
		{
			uint amount = price[price.Count - 1] * (uint)count;
			if (price.Count >= 5)
			{
				BaseGameItemInfo costItem = player.InventoryManager.GetNormalItemGDPL((ItemTypeEnum)price[0], price[1], price[2], price[3]);
				if (costItem == null || costItem.ItemCount < amount)
				{
					await CallGSRouter.SendScript(connection, "ShopLogic_BuyGoods", "{\"sErr\":\"tip.cash_not_enough\"}");
					return;
				}
				costItem.ItemCount -= amount;
				sync.Items.Add(costItem.ToProto());
			}
			else
			{
				uint cashId = price[0];
				if (cashId == 4)
				{
					if (player.Data.Vigor < amount)
					{
						await CallGSRouter.SendScript(connection, "ShopLogic_BuyGoods", "{\"sErr\":\"tip.cash_not_enough\"}");
						return;
					}
					player.Data.Vigor -= amount;
					// 第 432 节：消耗感知同步累积智识巡游度（感知巡游）。
					// 第 438 节：顺带把活动任务（Type 4）的进度也推进并一起下发。
					MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.AccrueFromVigor(player, amount, sync);
					TaskEventService.AddByType(player, 4, (int)amount);
				}
				else
				{
					uint sid = ((cashId == 1) ? 3u : (cashId * 2 + 1));
					PlayerAttr cashAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == sid);
					if (cashAttr == null || cashAttr.Val < amount)
					{
						await CallGSRouter.SendScript(connection, "ShopLogic_BuyGoods", "{\"sErr\":\"tip.cash_not_enough\"}");
						return;
					}
					cashAttr.Val -= amount;
					sync.Custom[player.ToPackedAttrKey(1u, sid)] = cashAttr.Val;
					sync.Custom[player.ToShiftedAttrKey(1u, sid)] = cashAttr.Val;
					if (cashId == 1)
					{
						foreach (var (key, value) in player.BuildMoneySync())
						{
							sync.Money[key] = value;
						}
					}
				}
			}
		}
		await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		TaskEventService.OnShopBuy(player, count);
		TaskEventService.SyncType(player, 8, sync);
		// 第 438 节：活动任务「购置年货 = 在商店购买任意道具」（Type 8）。
		MikuSB.GameServer.Game.Activity.ActivityQuestService.Add(player, 8, count, sync);
		uint goodsSid = (uint)(1000 + goodsId);
		PlayerStrAttr oldGoods = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 1 && x.Sid == goodsSid);
		JsonObject goodsJson = new JsonObject();
		if (!string.IsNullOrWhiteSpace(oldGoods?.Val))
		{
			try
			{
				goodsJson = JsonNode.Parse(oldGoods.Val)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				goodsJson = new JsonObject();
			}
		}
		int bought = goodsJson["buynum"]?.GetValue<int>() ?? 0;
		goodsJson["buynum"] = bought + count;
		goodsJson["tbgoods"] = new JsonArray();
		player.SetStrAttr(1u, goodsSid, goodsJson.ToJsonString());
		DatabaseHelper.SaveDatabaseType(player.Data);
		Console.WriteLine($"[SHOP_BUY] goodsId={goodsId} sid={goodsSid} buynum={bought + count} json={goodsJson.ToJsonString()}");
		sync.CustomStr[player.ToShiftedAttrKey(1u, goodsSid)] = goodsJson.ToJsonString();
		sync.CustomStr[player.ToPackedAttrKey(1u, goodsSid)] = goodsJson.ToJsonString();
		JsonArray row = new JsonArray();
		foreach (uint value2 in reward)
		{
			row.Add((int)value2);
		}
		JsonObject rsp = new JsonObject
		{
			["nGoodsId"] = goodsId,
			["tbGoods"] = row,
			["bIsSplit"] = false
		};
		await CallGSRouter.SendScript(connection, "ShopLogic_BuyGoods", rsp.ToJsonString(), sync);
	}

	private static int FindShopId(int goodsId)
	{
		string[] array = new string[2]
		{
			Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "goods.json"),
			Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_goods.json")
		};
		foreach (string path in array)
		{
			try
			{
				if (!File.Exists(path))
				{
					continue;
				}
				JsonArray jsonArray = ShopLogic_GetGoodsList.LoadRows(path);
				if (jsonArray == null)
				{
					continue;
				}
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject)
					{
						JsonNode? jsonNode = jsonObject["GoodsId"];
						if (jsonNode != null && jsonNode.GetValue<int>() == goodsId)
						{
							return jsonObject["ShopId"]?.GetValue<int>() ?? 0;
						}
					}
				}
			}
			catch
			{
			}
		}
		return 0;
	}

	private static List<uint>? FindPrice(int goodsId, int buyMode)
	{
		string[] array = new string[2]
		{
			Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "goods.json"),
			Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_goods.json")
		};
		foreach (string path in array)
		{
			try
			{
				if (!File.Exists(path))
				{
					continue;
				}
				JsonArray jsonArray = ShopLogic_GetGoodsList.LoadRows(path);
				if (jsonArray == null)
				{
					continue;
				}
				foreach (JsonNode item in jsonArray)
				{
					if (!(item is JsonObject jsonObject))
					{
						continue;
					}
					JsonNode? jsonNode = jsonObject["GoodsId"];
					if (jsonNode == null || jsonNode.GetValue<int>() != goodsId)
					{
						continue;
					}
					string text = ((buyMode == 2) ? "Price2" : "Price1");
					string text2 = jsonObject[text]?.ToString();
					if (string.IsNullOrWhiteSpace(text2) || text2 == "\"\"")
					{
						text = ((text == "Price1") ? "Price2" : "Price1");
						text2 = jsonObject[text]?.ToString();
					}
					if (!string.IsNullOrWhiteSpace(text2))
					{
						List<uint> list = (from m in Regex.Matches(text2, "\\d+")
							select uint.TryParse(m.Value, out var result) ? result : 0u into v
							where v != 0
							select v).ToList();
						if (list.Count >= 2)
						{
							return list;
						}
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static List<uint>? FindGdpln(int goodsId)
	{
		string[] array = new string[2]
		{
			Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "goods.json"),
			Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_goods.json")
		};
		foreach (string path in array)
		{
			try
			{
				if (!File.Exists(path))
				{
					continue;
				}
				JsonArray jsonArray = ShopLogic_GetGoodsList.LoadRows(path);
				if (jsonArray == null)
				{
					continue;
				}
				foreach (JsonNode item in jsonArray)
				{
					if (!(item is JsonObject jsonObject))
					{
						continue;
					}
					JsonNode? jsonNode = jsonObject["GoodsId"];
					if (jsonNode != null && jsonNode.GetValue<int>() == goodsId)
					{
						string input = jsonObject["GDPLN"]?.ToString() ?? "";
						List<uint> list = (from m in Regex.Matches(input, "\\d+")
							select uint.TryParse(m.Value, out var result) ? result : 0u into v
							where v != 0
							select v).Take(5).ToList();
						if (list.Count >= 5)
						{
							return list;
						}
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}
}
