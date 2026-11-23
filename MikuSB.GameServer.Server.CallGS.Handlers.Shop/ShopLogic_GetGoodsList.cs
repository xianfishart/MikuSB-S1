using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

[CallGSApi("ShopLogic_GetGoodsList")]
public class ShopLogic_GetGoodsList : ICallGSHandler
{
	private static JsonArray? _goods;

	private static JsonArray? _dlcGoods;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		// 第 354 节：切页签「一卡一卡」的耗时探针（读 → 抽卡 → 序列化 各段毫秒数）。
		Stopwatch sw = Stopwatch.StartNew();
		int shopId = 0;
		try
		{
			shopId = (JsonNode.Parse(param)?["nShopId"]?.GetValue<int>()).GetValueOrDefault();
		}
		catch
		{
		}
		if (shopId <= 0)
		{
			await CallGSRouter.SendScript(connection, "ShopLogic_GetGoodsList", "{\"shopId\":0,\"goodsList\":[]}");
			return;
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject saved = ReadShopData(player, shopId);
		long msRead = sw.ElapsedMilliseconds;
		JsonObject shopData = GetOrCreateStock(player, shopId, forceNew: false, sync, out JsonArray goods);
		long msStock = sw.ElapsedMilliseconds;
		// 第 354 节（2026-11-16）：切页签「一卡一卡」。
		// 原来响应里把同一个商品数组深拷贝成了 5 份（goodsList / tbGoods / tbShopGoods /
		// tbGoodsList / tbList），是早期为了赌客户端读哪个 key 加的多重保险。反汇编客户端
		// UMG.Shop.umg_shop::OnReceiveUpdate 确认它只读 shopId 和 goodsList 两个字段，
		// 其余四个根本没人读。而客户端是纯 Lua json.decode，实测：
		//   响应 109KB（2 份）→ 55ms；响应 381KB（5 份）→ 199ms。
		// 也就是说每切一次页签，客户端光解析这堆重复数据就要 200ms，肉眼就是「卡一下」。
		// 现在只保留 goodsList 一份，shop=21（100 条）响应从 272KB 降到约 55KB。
		JsonObject response = new JsonObject
		{
			["nShopId"] = shopId,
			["shopId"] = shopId,
			["nRefreshTime"] = 0,
			["nNextRefreshTime"] = 0,
			["refreshnum"] = AsInt(shopData["refreshnum"]),
			["goodsList"] = goods.DeepClone()
		};
		long msClone = sw.ElapsedMilliseconds;
		string payload = response.ToJsonString();
		long msJson = sw.ElapsedMilliseconds;
		if (msJson >= 20)
		{
			try
			{
				File.AppendAllText("D:\\DSH\\.tools\\shop_flow.log", $"{DateTime.Now:HH:mm:ss} SLOW shop={shopId} goods={goods.Count} read={msRead} stock={msStock - msRead} clone={msClone - msStock} json={msJson - msClone} total={msJson}ms bytes={payload.Length}\n");
			}
			catch
			{
			}
		}
		if (shopId == 404)
		{
			try
			{
				File.AppendAllText("D:\\DSH\\.tools\\shop404.log", $"{DateTime.Now:HH:mm:ss} savedTb={(saved["tbgoods"] as JsonArray)?.Count ?? (-1)} goods={goods.Count} refresh={AsInt(shopData["refreshnum"])}\n");
			}
			catch
			{
			}
		}
		sync.CustomStr[player.ToShiftedAttrKey(1u, (uint)shopId)] = shopData.ToJsonString();
		sync.CustomStr[player.ToPackedAttrKey(1u, (uint)shopId)] = shopData.ToJsonString();
		await CallGSRouter.SendScript(connection, "ShopLogic_GetGoodsList", payload, sync);
	}

	public static JsonObject GetOrCreateStock(PlayerInstance player, int shopId, bool forceNew, NtfSyncPlayer sync, out JsonArray goods)
	{
		JsonObject jsonObject = ReadShopData(player, shopId);
		(int, int, int, int) tuple = LoadShopInfo(shopId);
		JsonArray jsonArray = jsonObject["tbgoods"] as JsonArray;
		int num;
		if (tuple.Item1 > 0)
		{
			(num, _, _, _) = tuple;
		}
		else
		{
			num = 12;
		}
		int num2 = num;
		long num3 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		int num4 = AsInt(jsonObject["nextrefreshtime"]);
		int autoRefreshInterval = GetAutoRefreshInterval(shopId);
		bool flag = false;
		if (!forceNew && autoRefreshInterval > 0 && num4 > 0 && num3 >= num4)
		{
			forceNew = true;
			flag = true;
		}
		bool flag2 = !forceNew && jsonArray != null && (AsInt(jsonObject["version"]) != tuple.Item3 || AsInt(jsonObject["listversion"]) != tuple.Item4);
		if (flag2)
		{
			forceNew = true;
		}
		if (shopId == 404 && (jsonArray == null || jsonArray.Count != 15))
		{
			forceNew = true;
		}
		if (!forceNew && jsonArray != null && jsonArray.Count > 0 && (num2 <= 0 || jsonArray.Count <= num2))
		{
			JsonArray rebuilt = RebuildFromStock(shopId, jsonArray);
			if (rebuilt.Count > 0)
			{
				// 老存档里的 tbgoods 还是整行商品，顺手就地瘦身（下次进这个商店就变小）。
				if (NeedsSlim(jsonArray))
				{
					jsonObject["tbgoods"] = SlimStock(rebuilt);
					player.SetStrAttr(1u, (uint)shopId, jsonObject.ToJsonString());
				}
				goods = rebuilt;
				return jsonObject;
			}
			// 库存里的 goodsid 在配置表里全都找不到了（改表/换版本），当没有库存处理，走重抽。
		}
		JsonArray all = BuildGoodsCached(shopId);
		goods = PickWeighted(all, num2);
		ResetBuyCounts(player, all, sync);
		int num5 = AsInt(jsonObject["refreshnum"]);
		if (flag)
		{
			num5 = 0;
		}
		else if (forceNew && !flag2)
		{
			num5++;
		}
		jsonObject["version"] = tuple.Item3;
		jsonObject["listversion"] = tuple.Item4;
		jsonObject["refreshnum"] = num5;
		jsonObject["nextrefreshtime"] = ((autoRefreshInterval > 0) ? (num3 + autoRefreshInterval) : 0);
		// 第 354 节：库存只存 goodsid + discountindex（见 SlimStock 注释），别再把整行塞回存档。
		jsonObject["tbgoods"] = SlimStock(goods);
		player.SetStrAttr(1u, (uint)shopId, jsonObject.ToJsonString());
		return jsonObject;
	}

	// 第 354 节（2026-11-16）：切页签「一卡一卡」的真正元凶。
	// 客户端 Shop.Shop.lua 的 GetOnLineGoodsInfo(goodsId)（582~593 行）会先
	//     local data = ShopLogic.GetShopData(conf.nShopId)
	// 也就是 json.decode 整个商店存档 JSON，然后才在 data.tbgoods 里按 goodsid 找一条。
	// 而 uw_shop_itembox.ShowShopItem 对**每个商品格**都调一次它 —— 100 条商品的页签
	// 等于把同一个 54KB 的 JSON 反复 decode 30 多次，实测每次 12ms，一次切换就是 300ms+。
	// 客户端这段代码改不了，所以把存档里的 tbgoods 从「整行商品（20+ 字段）」压成
	// 「goodsid + discountindex」两个字段（GetOnLineGoodsInfo 只读这两个），
	// 54KB → 约 4KB，decode 12ms → 1ms 以内。
	// 返回给客户端的 goodsList 仍由 RebuildFromStock 从配置表重建完整行，界面不受影响。
	private static JsonArray SlimStock(JsonArray goods)
	{
		JsonArray slim = new JsonArray();
		foreach (JsonNode node in goods)
		{
			if (node is JsonObject row)
			{
				int id = AsInt(row["goodsid"] ?? row["GoodsId"]);
				if (id > 0)
				{
					slim.Add(new JsonObject
					{
						["goodsid"] = id,
						["discountindex"] = AsInt(row["discountindex"])
					});
				}
			}
		}
		return slim;
	}

	private static bool NeedsSlim(JsonArray stock)
	{
		foreach (JsonNode node in stock)
		{
			if (node is JsonObject row && row.Count > 3)
			{
				return true;
			}
		}
		return false;
	}

	public static JsonArray RebuildFromStock(int shopId, JsonArray stock)
	{
		JsonArray result = new JsonArray();
		Dictionary<int, JsonObject> map = new Dictionary<int, JsonObject>();
		foreach (JsonNode node in BuildGoodsCached(shopId))
		{
			if (node is JsonObject row)
			{
				int id = AsInt(row["goodsid"]);
				if (id > 0 && !map.ContainsKey(id))
				{
					map[id] = row;
				}
			}
		}
		foreach (JsonNode node in stock)
		{
			if (!(node is JsonObject item))
			{
				continue;
			}
			int id = AsInt(item["goodsid"]);
			if (id > 0 && map.TryGetValue(id, out JsonObject full))
			{
				JsonObject clone = (JsonObject)full.DeepClone();
				clone["discountindex"] = AsInt(item["discountindex"]);
				result.Add(clone);
			}
		}
		return result;
	}

	private static readonly Dictionary<int, JsonArray> _buildCache = new Dictionary<int, JsonArray>();

	private static JsonArray BuildGoodsCached(int shopId)
	{
		if (_buildCache.TryGetValue(shopId, out JsonArray cached))
		{
			return cached;
		}
		JsonArray built = BuildGoods(shopId);
		_buildCache[shopId] = built;
		return built;
	}

	public static void EnsureShopMetadata(PlayerInstance player)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "shop_tab.json");
			if (!File.Exists(path))
			{
				return;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					int shopId = AsInt(jsonObject["ShopId"]);
					if (shopId > 0 && string.IsNullOrWhiteSpace(player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 1 && x.Sid == (uint)shopId)?.Val))
					{
						JsonObject jsonObject2 = new JsonObject
						{
							["version"] = AsInt(jsonObject["Version"]),
							["listversion"] = AsInt(jsonObject["ListVersion"]),
							["refreshnum"] = 0,
							["tbgoods"] = new JsonArray()
						};
						player.SetStrAttr(1u, (uint)shopId, jsonObject2.ToJsonString());
					}
				}
			}
		}
		catch
		{
		}
	}

	// 201 节（2026-11-11）：401 是往期活动的「追忆精选」商店（客户端补丁把它改名为
	// 「限量供应」页签）。dlc_goods.json 里有 401 的 15 条商品，但原判断只放行
	// 101~148 / 404，导致 401 拿到空列表 —— 界面页签在、商品全空。
	private static bool IsDlcShop(int shopId)
	{
		return (shopId >= 101 && shopId <= 148) || shopId == 401 || shopId == 404;
	}

	public static (int Columns, int RefreshLimits, int Version, int ListVersion) LoadShopInfo(int shopId)
	{
		if (IsDlcShop(shopId))
		{
			return (Columns: 99, RefreshLimits: 5, Version: 0, ListVersion: 0);
		}
		// 第 354 节：shop_tab.json 有 298KB，原来每次请求都 ReadAllText + JsonNode.Parse 一遍，
		// 白烧 2~5ms。改成进程内缓存整表（改配置后重启生效，与其它表一致）。
		Dictionary<int, (int, int, int, int)> table = _shopInfoCache ??= LoadShopInfoTable();
		if (table.TryGetValue(shopId, out (int, int, int, int) info))
		{
			return (Columns: info.Item1, RefreshLimits: info.Item2, Version: info.Item3, ListVersion: info.Item4);
		}
		return (Columns: 12, RefreshLimits: 5, Version: 0, ListVersion: 0);
	}

	private static Dictionary<int, (int, int, int, int)>? _shopInfoCache;

	private static Dictionary<int, (int, int, int, int)> LoadShopInfoTable()
	{
		Dictionary<int, (int, int, int, int)> map = new Dictionary<int, (int, int, int, int)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "shop_tab.json");
			if (!File.Exists(path))
			{
				return map;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return map;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					int key = AsInt(jsonObject["ShopId"]);
					if (key > 0 && !map.ContainsKey(key))
					{
						map[key] = (AsInt(jsonObject["Columns"]), AsInt(jsonObject["RefreshLimits"]), AsInt(jsonObject["Version"]), AsInt(jsonObject["ListVersion"]));
					}
				}
			}
		}
		catch
		{
		}
		return map;
	}

	public static JsonObject ReadShopData(PlayerInstance player, int shopId)
	{
		PlayerStrAttr playerStrAttr = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 1 && x.Sid == (uint)shopId);
		if (string.IsNullOrWhiteSpace(playerStrAttr?.Val))
		{
			return new JsonObject();
		}
		try
		{
			return JsonNode.Parse(playerStrAttr.Val)?.AsObject() ?? new JsonObject();
		}
		catch
		{
			return new JsonObject();
		}
	}

	public static JsonArray BuildGoods(int shopId)
	{
		JsonArray jsonArray = new JsonArray();
		JsonArray jsonArray2 = LoadGoods();
		if (jsonArray2 == null)
		{
			return jsonArray;
		}
		JsonArray jsonArray3 = (IsDlcShop(shopId) ? LoadDlcGoods() : null);
		foreach (JsonNode item in jsonArray2.Concat(jsonArray3 ?? new JsonArray()))
		{
			if (item is JsonObject jsonObject && AsInt(jsonObject["ShopId"]) == shopId)
			{
				JsonObject jsonObject2 = (JsonObject)jsonObject.DeepClone();
				int num = AsInt(jsonObject["GoodsId"]);
				int num2 = AsInt(jsonObject["PosIndex"]);
				int num3 = AsInt(jsonObject["LimitType"]);
				int num4 = AsInt(jsonObject["LimitNum"]);
				jsonObject2["goodsid"] = num;
				jsonObject2["shopid"] = shopId;
				jsonObject2["discountindex"] = 0;
				jsonObject2["nId"] = num;
				jsonObject2["nGoodsId"] = num;
				jsonObject2["nItemId"] = num;
				jsonObject2["nShopId"] = shopId;
				jsonObject2["nPos"] = num2;
				jsonObject2["nPosIndex"] = num2;
				jsonObject2["nLimitType"] = num3;
				jsonObject2["nLimit"] = num4;
				jsonObject2["nLimitNum"] = num4;
				jsonObject2["nBuyCount"] = 0;
				jsonObject2["nBuyNum"] = 0;
				jsonObject2["nCount"] = 1;
				jsonObject2["nNum"] = 1;
				jsonObject2["nWeight"] = AsInt(jsonObject["Weight"]);
				jsonObject2["nPrice"] = jsonObject["Price1"]?.DeepClone();
				jsonObject2["nPrice1"] = jsonObject["Price1"]?.DeepClone();
				jsonObject2["nPrice2"] = jsonObject["Price2"]?.DeepClone();
				jsonObject2["nMoneyType"] = 2;
				jsonArray.Add(jsonObject2);
			}
		}
		return jsonArray;
	}

	public static JsonArray PickWeighted(JsonArray all, int columns)
	{
		List<JsonObject> list = all.OfType<JsonObject>().ToList();
		JsonArray jsonArray = new JsonArray();
		if (columns <= 0 || columns >= list.Count)
		{
			foreach (JsonObject item in list)
			{
				jsonArray.Add(item.DeepClone());
			}
			return jsonArray;
		}
		while (jsonArray.Count < columns && list.Count > 0)
		{
			int num = list.Sum((JsonObject x) => Math.Max(1, AsInt(x["nWeight"] ?? x["Weight"])));
			double num2 = Random.Shared.NextDouble() * (double)num;
			double num3 = 0.0;
			int index = list.Count - 1;
			for (int num4 = 0; num4 < list.Count; num4++)
			{
				num3 += (double)Math.Max(1, AsInt(list[num4]["nWeight"] ?? list[num4]["Weight"]));
				if (num2 < num3)
				{
					index = num4;
					break;
				}
			}
			jsonArray.Add(list[index].DeepClone());
			list.RemoveAt(index);
		}
		return jsonArray;
	}

	public static void ResetBuyCounts(PlayerInstance player, JsonArray all, NtfSyncPlayer sync)
	{
		foreach (JsonNode item in all)
		{
			if (item is JsonObject jsonObject)
			{
				int num = AsInt(jsonObject["nGoodsId"] ?? jsonObject["GoodsId"]);
				if (num > 0)
				{
					uint sid = (uint)(1000 + num);
					player.SetStrAttr(1u, sid, "{\"buynum\":0,\"tbgoods\":[]}");
					sync.CustomStr[player.ToShiftedAttrKey(1u, sid)] = "{\"buynum\":0,\"tbgoods\":[]}";
				}
			}
		}
	}

	public static int GetAutoRefreshInterval(int shopId)
	{
		bool flag;
		switch (shopId)
		{
		case 12:
			return 600;
		case 13:
		case 14:
		case 18:
		case 19:
		case 23:
		case 24:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return 86400;
		}
		return 0;
	}

	public static int AsInt(JsonNode? node)
	{
		if (node == null)
		{
			return 0;
		}
		if (node.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var value))
		{
			return value;
		}
		int result;
		return int.TryParse(node.ToString(), out result) ? result : 0;
	}

	private static JsonArray? LoadGoods()
	{
		if (_goods != null)
		{
			return _goods;
		}
		_goods = LoadRows(Path.Combine(Directory.GetCurrentDirectory(), "Resources", "shop", "goods.json"));
		return _goods;
	}

	private static JsonArray? LoadDlcGoods()
	{
		if (_dlcGoods != null)
		{
			return _dlcGoods;
		}
		_dlcGoods = LoadRows(Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_goods.json"));
		return _dlcGoods;
	}

	internal static JsonArray? LoadRows(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(path));
			if (jsonNode is JsonArray result)
			{
				return result;
			}
			if (jsonNode is JsonObject jsonObject)
			{
				JsonArray jsonArray = new JsonArray();
				foreach (KeyValuePair<string, JsonNode> item in jsonObject)
				{
					if (item.Value is JsonObject jsonObject2)
					{
						JsonObject jsonObject3 = (JsonObject)jsonObject2.DeepClone();
						if (AsInt(jsonObject3["GoodsId"]) <= 0 && int.TryParse(item.Key, out var result2))
						{
							jsonObject3["GoodsId"] = result2;
						}
						jsonArray.Add(jsonObject3);
					}
				}
				return jsonArray;
			}
		}
		catch
		{
		}
		return null;
	}
}
