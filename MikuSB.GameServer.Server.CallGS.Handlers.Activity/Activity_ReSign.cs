using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_ReSign")]
public class Activity_ReSign : CallGSHandler
{
	private static readonly Lazy<Dictionary<uint, (uint CurrencyId, uint Amount)>> CostCache = new Lazy<Dictionary<uint, (uint, uint)>>(LoadResignCosts);

	private static readonly Lazy<Dictionary<(uint ActivityId, uint DayId), List<List<uint>>>?> SignShortCache = new Lazy<Dictionary<(uint, uint), List<List<uint>>>>(LoadSignShortRows);

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint activityId = ReadUint(request["nId"]);
		uint awardType = ReadUint(request["nType"]);
		JsonObject response = new JsonObject();
		foreach (KeyValuePair<string, JsonNode> pair in request)
		{
			response[pair.Key] = pair.Value?.DeepClone();
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewarded = new JsonArray();
		if (activityId == 0 || (awardType != 0 && awardType != 2))
		{
			Logger.GetByClassName().Warn($"Activity_ReSign bad request (param='{param}') for uid={player.Uid}; answering without a reward.");
			response["tbRewards"] = rewarded;
			return CallGSResult.Ok(response.ToJsonString(), sync, "Activity_ReSign");
		}
		uint day = player.Attributes.GetValue(50u, ActivitySignService.SignedDaySid(activityId)) + 1;
		List<IReadOnlyList<uint>> rows = ResignRewardRows(activityId, day);
		if (rows == null || rows.Count == 0)
		{
			Logger.GetByClassName().Warn($"Activity_ReSign activity {activityId} day {day}: no sign reward row in sign/signday.json or sign/signshort.json; answering without a reward or a charge for uid={player.Uid}.");
		}
		else
		{
			var (currencyId, amount) = ResignCost(activityId);
			string error;
			if (currencyId == 0)
			{
				Logger.GetByClassName().Warn($"Activity_ReSign activity {activityId}: activity/activities.json has no CashID for this row, so the make-up sign price is unknown; refusing the claim for uid={player.Uid}.");
			}
			else if (!PortCurrency.TrySpend(player, currencyId, amount, sync, out error))
			{
				Logger.GetByClassName().Info($"Activity_ReSign activity {activityId}: uid={player.Uid} cannot pay {amount} x currency {currencyId} ({error}); refusing the claim.");
			}
			else
			{
				rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
				ActivitySignService.SetAttr(player, ActivitySignService.SignedDaySid(activityId), day, sync);
				Logger.GetByClassName().Info($"Activity {activityId} make-up sign-in for uid={player.Uid}: paid {amount} x currency {currencyId}, day -> {day}, granted {rewarded.Count} reward row(s).");
				DatabaseHelper.SaveDatabaseType(player.Data);
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
				DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
			}
		}
		response["tbRewards"] = rewarded;
		return CallGSResult.Ok(response.ToJsonString(), sync, "Activity_ReSign");
	}

	private static List<IReadOnlyList<uint>>? ResignRewardRows(uint activityId, uint day)
	{
		if (GameData.SignDayData.TryGetValue((activityId, day), out SignDayExcel value))
		{
			List<uint[]> list = DlcRuntimeOverrides.ReadRewardRows(value.SignReward);
			if (list.Count > 0)
			{
				return ((IEnumerable<uint[]>)list).Select((Func<uint[], IReadOnlyList<uint>>)((uint[] row) => row)).ToList();
			}
		}
		Dictionary<(uint, uint), List<List<uint>>> value2 = SignShortCache.Value;
		if (value2 != null && value2.TryGetValue((activityId, day), out var value3))
		{
			return ((IEnumerable<List<uint>>)value3).Select((Func<List<uint>, IReadOnlyList<uint>>)((List<uint> row) => row)).ToList();
		}
		return null;
	}

	private static (uint CurrencyId, uint Amount) ResignCost(uint activityId)
	{
		(uint, uint) value;
		return CostCache.Value.TryGetValue(activityId, out value) ? value : (CurrencyId: 0u, Amount: 1u);
	}

	private static Dictionary<uint, (uint CurrencyId, uint Amount)> LoadResignCosts()
	{
		Dictionary<uint, (uint, uint)> dictionary = new Dictionary<uint, (uint, uint)>();
		try
		{
			string text = Path.Combine(ConfigManager.Config.Path.ResourcePath, "activity", "activities.json");
			if (!File.Exists(text))
			{
				return dictionary;
			}
			if (!(JsonNode.Parse(File.ReadAllText(text)) is JsonArray jsonArray))
			{
				return dictionary;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					uint num = ReadUint(jsonObject["Id"]);
					uint num2 = ReadFirstUint(jsonObject["CashID"]);
					if (num != 0 && num2 != 0)
					{
						uint num3 = ReadFirstUint(jsonObject["CustomData"]);
						dictionary[num] = (num2, (num3 == 0) ? 1u : num3);
					}
				}
			}
			Logger.GetByClassName().Info($"Loaded {dictionary.Count} make-up sign price(s) from {text}.");
		}
		catch (Exception ex)
		{
			Logger.GetByClassName().Warn("activity/activities.json could not be read for make-up sign prices: " + ex.Message);
		}
		return dictionary;
	}

	private static Dictionary<(uint ActivityId, uint DayId), List<List<uint>>>? LoadSignShortRows()
	{
		try
		{
			string text = Path.Combine(ConfigManager.Config.Path.ResourcePath, "activity", "sign", "signshort.json");
			if (!File.Exists(text))
			{
				return null;
			}
			if (!(JsonNode.Parse(File.ReadAllText(text)) is JsonArray jsonArray))
			{
				return null;
			}
			Dictionary<(uint, uint), List<List<uint>>> dictionary = new Dictionary<(uint, uint), List<List<uint>>>();
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject))
				{
					continue;
				}
				uint num = ReadUint(jsonObject["ActivityId"]);
				uint num2 = ReadUint(jsonObject["DayId"]);
				if (num != 0 && num2 != 0)
				{
					List<List<uint>> list = ReadRewardRows(jsonObject["SignReward"]);
					if (list.Count > 0)
					{
						dictionary[(num, num2)] = list;
					}
				}
			}
			Logger.GetByClassName().Info($"Loaded {dictionary.Count} signshort reward row(s) from {text}.");
			return dictionary;
		}
		catch (Exception ex)
		{
			Logger.GetByClassName().Warn("sign/signshort.json could not be read: " + ex.Message);
			return null;
		}
	}

	private static List<List<uint>> ReadRewardRows(JsonNode? node)
	{
		List<List<uint>> list = new List<List<uint>>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonArray { Count: >=5 } jsonArray2))
			{
				continue;
			}
			List<uint> list2 = new List<uint>(5);
			bool flag = true;
			for (int i = 0; i < 5; i++)
			{
				if (!TryReadUint(jsonArray2[i], out var value))
				{
					flag = false;
					break;
				}
				list2.Add(value);
			}
			if (flag && list2[4] != 0)
			{
				list.Add(list2);
			}
		}
		return list;
	}

	private static uint ReadFirstUint(JsonNode? node)
	{
		if (node is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				uint num = ReadUint(item);
				if (num != 0)
				{
					return num;
				}
			}
			return 0u;
		}
		uint num2 = ReadUint(node);
		if (num2 != 0)
		{
			return num2;
		}
		string s = node?.ToString()?.Trim(new char[6] { ' ', '"', '{', '}', '[', ']' });
		uint result;
		return uint.TryParse(s, out result) ? result : 0u;
	}

	private static uint ReadUint(JsonNode? node)
	{
		uint value;
		return TryReadUint(node, out value) ? value : 0u;
	}

	private static bool TryReadUint(JsonNode? node, out uint value)
	{
		value = 0u;
		if (!(node is JsonValue jsonValue))
		{
			return false;
		}
		if (jsonValue.TryGetValue<uint>(out value))
		{
			return true;
		}
		return uint.TryParse(node.ToString(), out value);
	}
}
