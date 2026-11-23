using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Rogue;

public static class RogueService
{
	private static readonly Random Rng = new Random();

	public static uint DefaultActionTimes => 3u;

	public static uint DefaultGoodsPriceBuff => 100u;

	public static uint CurrencyId => 9u;

	public static IEnumerable<RogueActivityExcel> Activities => GameData.RogueActivityData.Values.OrderBy((RogueActivityExcel row) => row.Id);

	public static void SeedPinnedOpenStory(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (DlcEraState.CurrentEraId(player) == 1)
		{
			RogueActivityExcel rogueActivityExcel = SelectActivity(DateTime.Now);
			if (rogueActivityExcel != null)
			{
				SeedOpenStory(player, rogueActivityExcel, sync);
			}
		}
	}

	public static RogueActivityExcel? SelectActivity(DateTime now)
	{
		List<RogueActivityExcel> list = Activities.ToList();
		if (list.Count == 0)
		{
			return null;
		}
		long nowUnix = new DateTimeOffset(now).ToUnixTimeSeconds();
		foreach (RogueActivityExcel item in list)
		{
			long num = RogueJson.ToUnix(item.Start);
			long num2 = RogueJson.ToUnix(item.End);
			if ((num == 0L || nowUnix >= num) && (num2 == 0L || nowUnix < num2))
			{
				return item;
			}
		}
		List<RogueActivityExcel> list2 = list.Where((RogueActivityExcel row) =>
		{
			long num3 = RogueJson.ToUnix(row.Start);
			return num3 != 0L && nowUnix >= num3;
		}).ToList();
		RogueActivityExcel? result;
		if (list2.Count <= 0)
		{
			result = list[list.Count - 1];
		}
		else
		{
			result = list2.MaxBy((RogueActivityExcel row) => RogueJson.ToUnix(row.Start));
		}
		return result;
	}

	public static RogueActivityExcel? FindActivity(uint id)
	{
		RogueActivityExcel value;
		return GameData.RogueActivityData.TryGetValue(id, out value) ? value : null;
	}

	public static RogueMapExcel? FindNode(uint mapId, uint nodeId)
	{
		RogueMapExcel value;
		return GameData.RogueMapData.TryGetValue(mapId * 100000 + nodeId, out value) ? value : null;
	}

	public static uint CurrentMap(PlayerInstance player)
	{
		JsonObject baseInfo = ReadObject(player, 1u);
		uint num = MapId(baseInfo);
		if (num != 0)
		{
			return num;
		}
		RogueActivityExcel rogueActivityExcel = SelectActivity(DateTime.Now);
		return (rogueActivityExcel != null) ? SelectMapId(rogueActivityExcel) : 0u;
	}

	public static RogueLevelExcel? FindLevel(uint levelId)
	{
		RogueLevelExcel value;
		return GameData.RogueLevelData.TryGetValue(levelId, out value) ? value : null;
	}

	public static uint EntryNode(uint mapId)
	{
		List<RogueMapExcel> list = GameData.RogueMapData.Values.Where((RogueMapExcel row) => row.MapId == mapId).ToList();
		if (list.Count == 0)
		{
			return 0u;
		}
		RogueMapExcel rogueMapExcel = (from row in list
			where row.Node == 0
			orderby row.Id
			select row).FirstOrDefault();
		if (rogueMapExcel != null && rogueMapExcel.NextIds.Count > 0)
		{
			return rogueMapExcel.NextIds[0];
		}
		return (from row in list
			where row.Node != 0
			orderby row.Id
			select row.Id).FirstOrDefault();
	}

	public static uint SelectMapId(RogueActivityExcel act)
	{
		List<RogueMapExcel> list = GameData.RogueMapData.Values.Where((RogueMapExcel row) => row.MapId == act.MapId).ToList();
		uint result;
		if (list.Count > 0)
		{
			result = act.MapId;
		}
		else
		{
			result = ((GameData.RogueMapData.Keys.Count > 0) ? GameData.RogueMapData.Values.Min((RogueMapExcel row) => row.MapId) : act.MapId);
		}
		return result;
	}

	public static string? ReadString(PlayerInstance player, uint sid)
	{
		return player.Attributes.GetStringValue(25u, sid);
	}

	public static JsonObject ReadObject(PlayerInstance player, uint sid)
	{
		string text = ReadString(player, sid);
		if (string.IsNullOrWhiteSpace(text))
		{
			return new JsonObject();
		}
		try
		{
			return (JsonNode.Parse(text) as JsonObject) ?? new JsonObject();
		}
		catch
		{
			return new JsonObject();
		}
	}

	public static JsonArray ReadArray(PlayerInstance player, uint sid)
	{
		string text = ReadString(player, sid);
		if (string.IsNullOrWhiteSpace(text))
		{
			return new JsonArray();
		}
		try
		{
			return (JsonNode.Parse(text) as JsonArray) ?? new JsonArray();
		}
		catch
		{
			return new JsonArray();
		}
	}

	public static void Write(PlayerInstance player, uint sid, JsonNode value, NtfSyncPlayer? sync)
	{
		PlayerStrAttr attr = player.Attributes.SetString(25u, sid, value.ToJsonString());
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
	}

	public static JsonObject BuildBaseInfo(RogueActivityExcel act, uint mapId, uint curNode)
	{
		return new JsonObject
		{
			["nID"] = act.Id,
			["nMapID"] = mapId,
			["nCurNode"] = curNode,
			["nAvaActTimes"] = DefaultActionTimes,
			["nUpperActTimes"] = DefaultActionTimes,
			["nUsedActTimes"] = 0,
			["nRefreshTimes"] = 0,
			["nReviveTimes"] = 0,
			["nGoodsPriceBuff"] = DefaultGoodsPriceBuff
		};
	}

	public static void SeedOpenStory(PlayerInstance player, RogueActivityExcel act, NtfSyncPlayer? sync)
	{
		if (string.IsNullOrWhiteSpace(ReadString(player, 9999u)))
		{
			long num = RogueJson.ToUnix(act.Start);
			long num2 = RogueJson.ToUnix(act.End);
			long num3 = ((num > 0 && num2 > num) ? (num + (num2 - num) / 2) : new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds());
			PlayerStrAttr attr = player.Attributes.SetString(25u, 9999u, num3.ToString(CultureInfo.InvariantCulture));
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
	}

	public static (RogueActivityExcel Act, JsonObject BaseInfo)? EnsureStarted(PlayerInstance player, NtfSyncPlayer? sync)
	{
		RogueActivityExcel rogueActivityExcel = SelectActivity(DateTime.Now);
		if (rogueActivityExcel == null)
		{
			return null;
		}
		JsonObject jsonObject = ReadObject(player, 1u);
		if (jsonObject.Count == 0)
		{
			uint mapId = SelectMapId(rogueActivityExcel);
			jsonObject = BuildBaseInfo(rogueActivityExcel, mapId, EntryNode(mapId));
			Write(player, 1u, jsonObject, sync);
		}
		SeedOpenStory(player, rogueActivityExcel, sync);
		return (rogueActivityExcel, jsonObject);
	}

	public static uint CurrentNode(JsonObject baseInfo)
	{
		return baseInfo["nCurNode"]?.GetValue<uint>() ?? 0;
	}

	public static uint MapId(JsonObject baseInfo)
	{
		return baseInfo["nMapID"]?.GetValue<uint>() ?? 0;
	}

	public static uint Field(JsonObject baseInfo, string key)
	{
		return baseInfo[key]?.GetValue<uint>() ?? 0;
	}

	public static void SetField(JsonObject baseInfo, string key, long value)
	{
		baseInfo[key] = value;
	}

	public static JsonObject NodeState(JsonObject pathInfo, uint nodeId)
	{
		string propertyName = nodeId.ToString(CultureInfo.InvariantCulture);
		if (pathInfo[propertyName] is JsonObject result)
		{
			return result;
		}
		return (JsonObject)(pathInfo[propertyName] = new JsonObject { ["nState"] = 0 });
	}

	public static void SetNodeState(PlayerInstance player, uint nodeId, uint state, NtfSyncPlayer? sync)
	{
		JsonObject jsonObject = ReadObject(player, 2u);
		NodeState(jsonObject, nodeId)["nState"] = state;
		Write(player, 2u, jsonObject, sync);
	}

	public static void MoveNext(PlayerInstance player, uint nodeId, NtfSyncPlayer? sync)
	{
		RogueActivityExcel rogueActivityExcel = SelectActivity(DateTime.Now);
		if (rogueActivityExcel != null)
		{
			JsonObject jsonObject = ReadObject(player, 1u);
			if (jsonObject.Count == 0)
			{
				jsonObject = BuildBaseInfo(rogueActivityExcel, SelectMapId(rogueActivityExcel), nodeId);
			}
			SetField(jsonObject, "nCurNode", nodeId);
			Write(player, 1u, jsonObject, sync);
			SetNodeState(player, nodeId, 1u, sync);
		}
	}

	public static uint NewSeed()
	{
		return (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
	}

	public static JsonArray RollGoods(RogueActivityExcel act, uint nodeId)
	{
		List<uint> list = new List<uint>();
		if (GameData.RogueShopData.TryGetValue(act.ShopId, out RogueShopExcel value))
		{
			foreach (List<(uint, uint)> goodsPool in value.GoodsPools)
			{
				uint num = PickWeighted(goodsPool);
				if (num != 0 && !list.Contains(num))
				{
					list.Add(num);
				}
			}
		}
		JsonArray jsonArray = new JsonArray();
		foreach (uint item in list)
		{
			jsonArray.Add(new JsonObject
			{
				["nGoodsID"] = item,
				["nBuyState"] = 0
			});
		}
		return jsonArray;
	}

	private static uint PickWeighted(List<(uint Id, uint Weight)> pool)
	{
		if (pool.Count == 0)
		{
			return 0u;
		}
		long num = pool.Sum(((uint Id, uint Weight) entry) => entry.Weight);
		if (num <= 0)
		{
			return pool[0].Id;
		}
		long num2 = Rng.NextInt64(num);
		foreach (var item3 in pool)
		{
			uint item = item3.Id;
			uint item2 = item3.Weight;
			num2 -= item2;
			if (num2 < 0)
			{
				return item;
			}
		}
		return pool[pool.Count - 1].Id;
	}

	public static void StoreShopGoods(PlayerInstance player, uint nodeId, JsonArray goods, NtfSyncPlayer? sync)
	{
		JsonObject jsonObject = ReadObject(player, 10u);
		jsonObject[nodeId.ToString(CultureInfo.InvariantCulture)] = goods;
		Write(player, 10u, jsonObject, sync);
	}

	public static void EnsureShopGoods(PlayerInstance player, RogueActivityExcel act, uint nodeId, NtfSyncPlayer? sync)
	{
		JsonObject jsonObject = ReadObject(player, 10u);
		string propertyName = nodeId.ToString(CultureInfo.InvariantCulture);
		if (!(jsonObject[propertyName] is JsonArray { Count: >0 }))
		{
			jsonObject[propertyName] = RollGoods(act, nodeId);
			Write(player, 10u, jsonObject, sync);
		}
	}

	public static JsonArray ConvertFormation(JsonArray? incoming)
	{
		JsonArray jsonArray = new JsonArray();
		for (int i = 0; i < 3; i++)
		{
			JsonArray jsonArray2 = ((incoming != null && i < incoming.Count) ? (incoming[i] as JsonArray) : null);
			uint num = ((jsonArray2 != null && jsonArray2.Count > 0) ? AsUInt(jsonArray2[0]) : 0u);
			bool flag = jsonArray2 != null && jsonArray2.Count > 1 && AsBool(jsonArray2[1]);
			if (num == 0)
			{
				jsonArray.Add(new JsonObject
				{
					["RoleID"] = 0,
					["nGot"] = 0
				});
			}
			else
			{
				jsonArray.Add(new JsonObject
				{
					["RoleID"] = num,
					["nGot"] = ((!flag) ? 1 : 0)
				});
			}
		}
		return jsonArray;
	}

	private static uint AsUInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<uint>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<int>(out var value2) && value2 > 0)
			{
				return (uint)value2;
			}
			if (jsonValue.TryGetValue<string>(out string value3) && uint.TryParse(value3, out var result))
			{
				return result;
			}
		}
		return 0u;
	}

	private static bool AsBool(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<bool>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<uint>(out var value2))
			{
				return value2 != 0;
			}
		}
		return false;
	}

	public static void ApplyHealth(PlayerInstance player, JsonNode? tbHp, NtfSyncPlayer? sync)
	{
		if (!(tbHp is JsonObject { Count: not 0 } jsonObject))
		{
			return;
		}
		JsonObject jsonObject2 = ReadObject(player, 3u);
		foreach (var (text2, node) in jsonObject)
		{
			if (uint.TryParse(text2, out var _))
			{
				JsonObject jsonObject3 = (jsonObject2[text2] as JsonObject) ?? new JsonObject
				{
					["RoleID"] = 0,
					["nGot"] = 0
				};
				uint num = AsUInt(node);
				jsonObject3["nCurHP"] = num;
				jsonObject3["nIsDead"] = ((num == 0) ? 1 : 0);
				jsonObject2[text2] = jsonObject3;
			}
		}
		Write(player, 3u, jsonObject2, sync);
	}

	public static void ReviveFirstDead(PlayerInstance player, NtfSyncPlayer? sync)
	{
		JsonObject jsonObject = ReadObject(player, 3u);
		foreach (var (propertyName, jsonNode2) in jsonObject)
		{
			if (!(jsonNode2 is JsonObject jsonObject2) || AsUInt(jsonObject2["nIsDead"]) == 0)
			{
				continue;
			}
			jsonObject2["nIsDead"] = 0;
			jsonObject2["nCurHP"] = 100;
			jsonObject[propertyName] = jsonObject2;
			break;
		}
		Write(player, 3u, jsonObject, sync);
	}

	public static void Reset(PlayerInstance player, NtfSyncPlayer? sync)
	{
		RogueActivityExcel rogueActivityExcel = SelectActivity(DateTime.Now);
		if (rogueActivityExcel != null)
		{
			Write(player, 2u, new JsonObject(), sync);
			Write(player, 3u, new JsonObject(), sync);
			Write(player, 4u, new JsonArray(), sync);
			Write(player, 10u, new JsonObject(), sync);
			Write(player, 11u, new JsonArray(), sync);
			Write(player, 5u, new JsonArray(), sync);
			uint mapId = SelectMapId(rogueActivityExcel);
			JsonObject value = BuildBaseInfo(rogueActivityExcel, mapId, EntryNode(mapId));
			Write(player, 1u, value, sync);
		}
	}
}
