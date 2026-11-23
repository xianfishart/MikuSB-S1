using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GiveGiftToArea")]
public class HouseGiveGift : IHouseFuncHandler
{
	private static Dictionary<int, uint>? _giftFavor;

	/// <summary>
	/// 2026-11-13 追加（修复记录第 282 节）：礼物物品 GDPL → gift.json 的 GiftId（= dorm_gift.json 的 Param1）。
	/// 旧代码用 Detail*100+Particular 拼 GiftId，324 件礼物里有 69 件对不上
	/// （例：[8,2,13,3] 正确是 221，算出来是 213），会查到别的礼物的好感配置或查不到。
	/// </summary>
	private static Dictionary<(uint G, uint D, uint P, uint L), int>? _giftIds;

	/// <summary>2026-11-13 追加（修复记录第 282 节）：gift.json 的 AddHouseLoveNum（房间好感），与 AddLoveNum 分开缓存。</summary>
	private static Dictionary<int, uint>? _giftHouseFavor;

	private static readonly Dictionary<(uint G, uint D, uint P, uint L), int> _furnitureTmpIds = new Dictionary<(uint, uint, uint, uint), int>();

	private static readonly Dictionary<(int AreaId, int TmpId), int> _support = new Dictionary<(int, int), int>();

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
		int areaId = HouseJson.NumField(request, "AreaId");
		JsonArray gifts = request["tbGDPLN"] as JsonArray;
		JsonArray added = new JsonArray();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		// 2026-11-13（修复记录第 283 节）：先把「已赠送但好感没加上」的历史礼物补回来。
		// 幂等（Config/Database/giftfavor_<uid>.json 记标记），重复送礼不会重复加。
		await HouseGiftRepair.RepairAsync(connection, sync);
		int giftId = 0;
		if (gifts != null)
		{
			foreach (JsonNode node in gifts)
			{
				JsonArray row = node as JsonArray;
				if (row == null || row.Count < 5)
				{
					continue;
				}
				uint g = ReadUInt(row[0]);
				uint d = ReadUInt(row[1]);
				uint p = ReadUInt(row[2]);
				uint l = ReadUInt(row[3]);
				uint count = Math.Max(1u, ReadUInt(row[4]));
				if (giftId == 0)
				{
					giftId = FindFurnitureTmpId(g, d, p, l);
				}
				BaseGameItemInfo item = FindItem(player, g, d, p, l);
				if (item != null)
				{
					if (item.ItemCount < count)
					{
						continue;
					}
					item.ItemCount -= count;
					if (item.ItemCount == 0)
					{
						player.InventoryManager.InventoryData.Items.Remove(item.UniqueId);
					}
					sync.Items.Add(item.ToProto());
				}
				BaseGameItemInfo furniture = await player.InventoryManager.AddHouseFurnitureItem((ItemTypeEnum)g, d, p, l, sendPacket: false);
				if (furniture != null)
				{
					sync.Items.Add(furniture.ToProto());
				}
				// 2026-11-13（修复记录第 288 节）：AddFurList 的元素必须是「家具 ID 数字」，不能是 GDPL 数组。
				// 客户端 HouseGiftLogic:OnGiveGiftSuccess 里
				//   sendKey = self:GetSendGiftKey(param.AddFurList[1], AreaId)
				// 而 GetSendGiftKey 第一句是 tbFurnitureToGift[t[1]] → tbGift[giftId]；
				// 该表是恒等映射（探针实测 f2g[2002]=2002、f2g[8]=8），键就是家具 ID。
				// 旧代码发 {g,d,p,l,1} 数组时 t[1]=Genre(8) → 查到别的礼物 → supportTargets2 无本区 → 返回 nil，
				// 于是 bedRoomMgr:OnGiveGiftSuccess 的 sendKey 为空，收礼动画和角色对话都不播（好感仍加）。
				// 改发家具 ID（= gift.json 的 FurnitureTmpId，也就是 dorm_gift.json 的 Param1）后
				// GetSendGiftKey(2002, 20) = "house.SendMessage2002"，动画链路恢复。
				int furTmpId = FindFurnitureTmpId(g, d, p, l);
				added.Add(furTmpId);
				await MarkFurnitureCollected(connection, areaId, g, d, p, l, sync);
			}
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		uint favorAdded = 0u;
		int preLevel = 0;
		int newLevel = 0;
		uint houseFavorAdded = 0u;
		int housePreLevel = 0;
		int houseNewLevel = 0;
		// 2026-11-13（修复记录第 284 节）：卧室场景里 AreaId 就是少女 ID（<90 的家具位落在该少女自己属性块），
		// 51/60/70/80/90 等公共区域没有单一少女，只加氛围值。
		int girlId = HouseAttr.GirlInArea(player, areaId);
		if (gifts != null && gifts.Count > 0 && areaId > 0 && areaId <= 100)
		{
			foreach (JsonNode node2 in gifts)
			{
				JsonArray row2 = node2 as JsonArray;
				if (row2 != null && row2.Count >= 4)
				{
					// 2026-11-13（修复记录第 282 节）：GiftId 改由 dorm_gift.json 的 Param1 取，
					// 同时把 AddLoveNum（角色好感）与 AddHouseLoveNum（房间好感）分开读。
					uint g2 = ReadUInt(row2[0]);
					uint d2 = ReadUInt(row2[1]);
					uint p2 = ReadUInt(row2[2]);
					uint l2 = ReadUInt(row2[3]);
					uint points = GiftFavor(g2, d2, p2, l2);
					uint housePoints = GiftHouseFavor(g2, d2, p2, l2);
					bool done = false;
					if (points != 0 && girlId > 0)
					{
						(uint Added, int PreLevel, int NewLevel) result = await HouseFavor.Award(connection, girlId, points, sync);
						favorAdded += result.Added;
						preLevel = result.PreLevel;
						newLevel = result.NewLevel;
						done = true;
					}
					if (housePoints != 0)
					{
						// 氛围值礼物（GiftId 221 / 2011 等，AddHouseLoveNum=4000）：写入基地好感 3100。
						(uint Added, int PreLevel, int NewLevel) house = await HouseFavor.AwardHouse(connection, housePoints, sync);
						houseFavorAdded += house.Added;
						housePreLevel = house.PreLevel;
						houseNewLevel = house.NewLevel;
						done = true;
					}
					if (done)
					{
						break;
					}
				}
			}
		}
		JsonObject response = new JsonObject
		{
			["FuncName"] = "GiveGiftToAreaSuccess",
			["bSuccess"] = true,
			["nResult"] = 0,
			["AreaId"] = areaId,
			["GirlId"] = girlId,
			["GiftId"] = giftId,
			["AddFurList"] = added
		};
		HouseFavor.AddFavorFields(response, preLevel, newLevel, favorAdded);
		HouseFavor.AddHouseFavorFields(response, housePreLevel, houseNewLevel, houseFavorAdded);
		TaskEventService.OnFavorChanged(connection.Player);
		TaskEventService.RefreshSnapshot(connection.Player, 121, 122, 123, 124);
		await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
	}

	private static void EnsureGiftTables()
	{
		if (_giftFavor != null)
		{
			return;
		}
		_giftFavor = new Dictionary<int, uint>();
		_giftHouseFavor = new Dictionary<int, uint>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "gift.json");
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject)
					{
						int num = HouseJson.NumField(jsonObject, "GiftId");
						if (num <= 0)
						{
							continue;
						}
						if (uint.TryParse(jsonObject["AddLoveNum"]?.ToString(), out var love))
						{
							_giftFavor[num] = love;
						}
						if (uint.TryParse(jsonObject["AddHouseLoveNum"]?.ToString(), out var houseLove))
						{
							_giftHouseFavor[num] = houseLove;
						}
					}
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>礼物物品 GDPL → gift.json 的 GiftId（取自 dorm_gift.json 的 Param1）。</summary>
	private static int GiftIdOf(uint g, uint d, uint p, uint l)
	{
		if (_giftIds == null)
		{
			_giftIds = new Dictionary<(uint, uint, uint, uint), int>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "item", "templates", "dorm_gift.json");
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (item is JsonObject obj)
							{
								int num = HouseJson.NumField(obj, "Param1");
								if (num > 0)
								{
									_giftIds[(ReadUInt(obj["Genre"]), ReadUInt(obj["Detail"]), ReadUInt(obj["Particular"]), ReadUInt(obj["Level"]))] = num;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		if (_giftIds.TryGetValue((g, d, p, l), out var value))
		{
			return value;
		}
		return (int)(d * 100 + p);
	}

	/// <summary>加角色好感（gift.json 的 AddLoveNum）。</summary>
	private static uint GiftFavor(uint g, uint d, uint p, uint l)
	{
		try
		{
			EnsureGiftTables();
			return _giftFavor!.GetValueOrDefault(GiftIdOf(g, d, p, l));
		}
		catch
		{
			return 0u;
		}
	}

	/// <summary>加房间/基地好感（gift.json 的 AddHouseLoveNum）。</summary>
	private static uint GiftHouseFavor(uint g, uint d, uint p, uint l)
	{
		try
		{
			EnsureGiftTables();
			return _giftHouseFavor!.GetValueOrDefault(GiftIdOf(g, d, p, l));
		}
		catch
		{
			return 0u;
		}
	}

	public static async Task MarkFurnitureCollected(Connection connection, int areaId, uint g, uint d, uint p, uint l, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int furnitureTmpId = FindFurnitureTmpId(g, d, p, l);
		if (furnitureTmpId != 0)
		{
			Dictionary<(int AreaId, int TmpId), int> support = LoadSupport();
			if (support.TryGetValue((areaId, furnitureTmpId), out var index) && index >= 0)
			{
				(uint, int) tuple = FurSlot(areaId, index);
				uint sid = tuple.Item1;
				int shift = tuple.Item2;
				uint value = HouseAttr.Read(player, sid) | (uint)(1 << shift);
				await HouseAttr.SetAsync(connection, sid, value, sync);
			}
		}
	}

	public static (uint Sid, int Shift) FurSlot(int areaId, int index)
	{
		uint item;
		if (areaId == 51)
		{
			item = (uint)(10010 + index / 10);
		}
		else if (areaId == 60)
		{
			item = (uint)(15001 + index / 10);
		}
		else if (areaId == 70)
		{
			item = (uint)(16101 + index / 10);
		}
		else if (areaId == 80)
		{
			item = (uint)(17101 + index / 10);
		}
		else
		{
			item = (uint)((areaId < 90 && areaId != 0) ? (Math.Max(0, areaId) * 50 + 10 + index / 10) : (30000 + AreaSort(areaId) * 10 + index / 10));
		}
		return (Sid: item, Shift: index % 10 * 3);
	}

	public static Task MarkCollectedForPlayer(PlayerInstance player, uint g, uint d, uint p, uint l, NtfSyncPlayer sync)
	{
		int tmpId = FindFurnitureTmpId(g, d, p, l);
		if (tmpId == 0)
		{
			return Task.CompletedTask;
		}
		KeyValuePair<(int, int), int> keyValuePair = LoadSupport().FirstOrDefault((KeyValuePair<(int AreaId, int TmpId), int> x) => x.Key.TmpId == tmpId);
		if (keyValuePair.Key.Item2 == 0)
		{
			return Task.CompletedTask;
		}
		(uint, int) tuple = FurSlot(keyValuePair.Key.Item1, keyValuePair.Value);
		uint sid = tuple.Item1;
		int item = tuple.Item2;
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = (uint)(1 << item)
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val |= (uint)(1 << item);
		}
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = playerAttr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.CompletedTask;
	}

	public static async Task MarkFurnitureCollectedByTemplate(Connection connection, uint g, uint d, uint p, uint l, NtfSyncPlayer sync)
	{
		int tmpId = FindFurnitureTmpId(g, d, p, l);
		if (tmpId != 0)
		{
			KeyValuePair<(int AreaId, int TmpId), int> loc = LoadSupport().FirstOrDefault((KeyValuePair<(int AreaId, int TmpId), int> x) => x.Key.TmpId == tmpId);
			if (loc.Key.TmpId != 0)
			{
				await MarkFurnitureCollected(connection, loc.Key.AreaId, g, d, p, l, sync);
			}
		}
	}

	private static int FindFurnitureTmpId(uint g, uint d, uint p, uint l)
	{
		if (_furnitureTmpIds.TryGetValue((g, d, p, l), out var value))
		{
			return value;
		}
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "item";
			buffer[3] = "templates";
			buffer[4] = "dorm_gift.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return 0;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item5 in jsonArray)
				{
					if (item5 is JsonObject jsonObject)
					{
						uint item = ReadUInt(jsonObject["Genre"]);
						uint item2 = ReadUInt(jsonObject["Detail"]);
						uint item3 = ReadUInt(jsonObject["Particular"]);
						uint item4 = ReadUInt(jsonObject["Level"]);
						int num = (int)ReadUInt(jsonObject["Param1"]);
						if (num > 0)
						{
							_furnitureTmpIds[(item, item2, item3, item4)] = num;
						}
					}
				}
			}
		}
		catch
		{
		}
		return _furnitureTmpIds.GetValueOrDefault((g, d, p, l));
	}

	private static int AreaSort(int areaId)
	{
		if (1 == 0)
		{
		}
		int result = areaId switch
		{
			0 => 0, 
			51 => 1, 
			60 => 2, 
			70 => 3, 
			80 => 4, 
			90 => 5, 
			_ => 0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static Dictionary<(int AreaId, int TmpId), int> LoadSupport()
	{
		if (_support.Count > 0)
		{
			return _support;
		}
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "support_furniture.json");
			if (!File.Exists(path))
			{
				return _support;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject obj)
					{
						int num = HouseJson.NumField(obj, "AreaId");
						int value = HouseJson.NumField(obj, "Index");
						int num2 = HouseJson.NumField(obj, "FurnitureTmpId");
						if (num > 0 && num2 > 0)
						{
							_support[(num, num2)] = value;
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

	private static BaseGameItemInfo? FindItem(PlayerInstance player, uint g, uint d, uint p, uint l)
	{
		ulong id = GameResourceTemplateId.FromGdpl(g, d, p, l);
		return player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == id);
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<uint>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (uint)Math.Max(0, value2);
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return (uint)Math.Max(0L, value3);
			}
		}
		return 0u;
	}
}
