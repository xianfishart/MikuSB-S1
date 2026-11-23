using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ExchangeGift")]
public class HouseGiftExchange : IHouseFuncHandler
{
	private const uint HouseGroupId = 101u;

	private const uint GiftShopBuyNumStart = 3135u;

	private const uint FurnitureMoneySid = 27u;

	private const uint DataGoldSid = 5u;

	private static Dictionary<int, int>? _giftTmp;

	private static Dictionary<(int Area, int Tmp), int>? _support;

	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		JsonObject request = (root["tbParam"] as JsonObject) ?? root;
		int index = HouseJson.NumField(request, "Index");
		JsonNode jsonNode = request["UseGold"];
		bool g = default;
		bool useGold = (jsonNode is JsonValue goldNode && goldNode.TryGetValue<bool>(out g)) & g;
		JsonObject row = LoadRow(index);
		if (row == null)
		{
			await Send(connection, success: false, null, "tip.exchange_invaild");
			return;
		}
		int shopType = HouseJson.NumField(row, "ShopType");
		int giftId = HouseJson.NumField(row, "GiftId");
		JsonArray gift = row["Gift"] as JsonArray;
		if (gift == null || gift.Count < 4)
		{
			await Send(connection, success: false, null, "tip.exchange_invaild");
			return;
		}
		uint genre = ReadUInt(gift[0]);
		uint detail = ReadUInt(gift[1]);
		uint particular = ReadUInt(gift[2]);
		uint level = ReadUInt(gift[3]);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray needItems = null;
		int limitId = HouseJson.NumField(row, "LimitId");
		int limitCount = HouseJson.NumField(row, "LimitCount");
		uint limitSid = ((limitId == 1) ? 136u : ((uint)(3135 + limitId)));
		if (limitId > 0 && limitCount > 0 && HouseAttr.Read(player, limitSid) >= limitCount)
		{
			await Send(connection, success: false, null, "tip.exchange_limit");
			return;
		}
		if (needItems != null && !HasNeedItems(player, needItems))
		{
			await Send(connection, success: false, null, "tip.material_not_enough");
			return;
		}
		int needMoney = HouseJson.NumField(row, "NeedMoney");
		int needGold = HouseJson.NumField(row, "NeedGold");
		if (useGold)
		{
			PlayerAttr gold = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == 5);
			if (needGold > 0 && (gold == null || gold.Val < (uint)needGold))
			{
				await Send(connection, success: false, null, "tip.cash_not_enough");
				return;
			}
			if (gold != null && needGold > 0)
			{
				gold.Val -= (uint)needGold;
				sync.Custom[player.ToPackedAttrKey(1u, 5u)] = gold.Val;
				sync.Custom[player.ToShiftedAttrKey(1u, 5u)] = gold.Val;
			}
		}
		else
		{
			PlayerAttr money = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 1 && x.Sid == 27);
			if (needMoney > 0 && (money == null || money.Val < (uint)needMoney))
			{
				await Send(connection, success: false, null, "tip.cash_not_enough");
				return;
			}
			if (money != null && needMoney > 0)
			{
				money.Val -= (uint)needMoney;
				sync.Custom[player.ToPackedAttrKey(1u, 27u)] = money.Val;
				sync.Custom[player.ToShiftedAttrKey(1u, 27u)] = money.Val;
			}
		}
		if (needItems != null)
		{
			foreach (JsonNode node in needItems)
			{
				JsonArray item = node as JsonArray;
				if (item == null || item.Count < 5)
				{
					continue;
				}
				List<uint> cost = item.Select(ReadUInt).Take(5).ToList();
				if (cost.Count >= 5 && cost[0] != 0 && cost[4] != 0)
				{
					BaseGameItemInfo inv = FindItem(player, cost[0], cost[1], cost[2], cost[3]);
					if (inv != null)
					{
						Consume(player, inv, cost[4], sync);
					}
				}
			}
		}
		JsonArray award = new JsonArray { genre, detail, particular, level, 1u };
		if (shopType != 2)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, new List<uint> { genre, detail, particular, level, 1u });
		}
		else
		{
			BaseGameItemInfo furniture = await player.InventoryManager.AddHouseFurnitureItem((ItemTypeEnum)genre, detail, particular, level, sendPacket: false);
			if (furniture != null)
			{
				sync.Items.Add(furniture.ToProto());
			}
			// 2026-11-13（修复记录第 287 节）：原本这里会调 MarkFurnitureByGiftId 把家具槽直接置位，
			// 表现成「商店买完礼物＝已经送给少女了」（房间自动摆上、赠送界面直接变灰、好感却没加）。
			// 按需求改为：购买一律只进背包，是否赠送只由赠送界面的 GiveGiftToArea 决定。
		}
		if (limitId > 0)
		{
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == limitSid);
			if (attr == null)
			{
				attr = new PlayerAttr
				{
					Gid = 101u,
					Sid = limitSid,
					Val = 0u
				};
				player.Data.Attrs.Add(attr);
			}
			attr.Val++;
			sync.Custom[player.ToPackedAttrKey(101u, attr.Sid)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(101u, attr.Sid)] = attr.Val;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		JsonObject response = new JsonObject
		{
			["FuncName"] = "ExchangeGiftRsp",
			["bSuccess"] = true,
			["nResult"] = 0,
			["Award"] = new JsonArray { award }
		};
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}

	private static bool HasNeedItems(PlayerInstance player, JsonArray needItems)
	{
		foreach (JsonNode needItem in needItems)
		{
			if (!(needItem is JsonArray { Count: >=5 } jsonArray))
			{
				continue;
			}
			List<uint> list = jsonArray.Select(ReadUInt).Take(5).ToList();
			if (list.Count >= 5 && list[0] != 0 && list[4] != 0)
			{
				BaseGameItemInfo baseGameItemInfo = FindItem(player, list[0], list[1], list[2], list[3]);
				if (baseGameItemInfo == null || baseGameItemInfo.ItemCount < list[4])
				{
					return false;
				}
			}
		}
		return true;
	}

	private static BaseGameItemInfo? FindItem(PlayerInstance player, uint g, uint d, uint p, uint l)
	{
		ulong id = GameResourceTemplateId.FromGdpl(g, d, p, l);
		return player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == id);
	}

	private static void Consume(PlayerInstance player, BaseGameItemInfo item, uint count, NtfSyncPlayer sync)
	{
		item.ItemCount -= count;
		if (item.ItemCount == 0)
		{
			player.InventoryManager.InventoryData.Items.Remove(item.UniqueId);
		}
		sync.Items.Add(item.ToProto());
	}

	private static async Task MarkFurnitureByGiftId(Connection connection, int giftId, NtfSyncPlayer sync)
	{
		int tmp = LoadGiftTmp().GetValueOrDefault(giftId);
		if (tmp != 0)
		{
			KeyValuePair<(int Area, int Tmp), int> loc = LoadSupport().FirstOrDefault((KeyValuePair<(int Area, int Tmp), int> x) => x.Key.Tmp == tmp);
			if (loc.Key.Tmp != 0)
			{
				(int, int) key = loc.Key;
				int areaId = key.Item1;
				int index = key.Item2;
				(uint, int) tuple = HouseGiveGift.FurSlot(areaId, index);
				uint sid = tuple.Item1;
				int shift = tuple.Item2;
				uint value = HouseAttr.Read(connection.Player, sid) | (uint)(1 << shift);
				await HouseAttr.SetAsync(connection, sid, value, sync);
			}
		}
	}

	private static Dictionary<int, int> LoadGiftTmp()
	{
		if (_giftTmp != null)
		{
			return _giftTmp;
		}
		_giftTmp = new Dictionary<int, int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "gift.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						int num = HouseJson.NumField(obj, "GiftId");
						int num2 = HouseJson.NumField(obj, "FurnitureTmpId");
						if (num > 0 && num2 > 0)
						{
							_giftTmp[num] = num2;
						}
					}
				}
			}
		}
		catch
		{
		}
		return _giftTmp;
	}

	private static Dictionary<(int Area, int Tmp), int> LoadSupport()
	{
		if (_support != null)
		{
			return _support;
		}
		_support = new Dictionary<(int, int), int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "support_furniture.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						int num = HouseJson.NumField(obj, "AreaId");
						int num2 = HouseJson.NumField(obj, "FurnitureTmpId");
						int num3 = HouseJson.NumField(obj, "Index");
						if (num > 0 && num2 > 0 && num3 > 0)
						{
							_support[(num, num2)] = num3;
						}
					}
				}
			}
		}
		catch
		{
		}
		return _support;
	}

	private static JsonObject? LoadRow(int index)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "gift_exchange.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			return (jsonArray != null) ? jsonArray.OfType<JsonObject>().FirstOrDefault((JsonObject x) => HouseJson.NumField(x, "Index") == index) : null;
		}
		catch
		{
			return null;
		}
	}

	private static async Task Send(Connection connection, bool success, JsonArray? award, string error)
	{
		JsonObject response = new JsonObject
		{
			["FuncName"] = "ExchangeGiftRsp",
			["bSuccess"] = success,
			["nResult"] = ((!success) ? 1 : 0),
			["sErr"] = error,
			["Award"] = award ?? new JsonArray()
		};
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString());
	}

	private static uint ReadUInt(JsonNode? node)
	{
		uint value;
		return (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out value)) ? value : 0u;
	}
}
