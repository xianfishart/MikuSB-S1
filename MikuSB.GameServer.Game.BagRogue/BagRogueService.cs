using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using MikuSB.Proto;
using MikuSB.Util;
using S1Admin;

namespace MikuSB.GameServer.Game.BagRogue;

public static class BagRogueService
{
	internal sealed record JigsawGoods(uint Id, uint Quality, uint Weight, uint AffixGroup, uint Price);

	private const uint Gid = 142u;

	private const uint StrGid = 61u;

	public const uint EventFight = 1u;

	public const uint EventAdventure = 2u;

	public const uint EventStore = 3u;

	public const uint EventRandom = 4u;

	public const uint EventBoss = 5u;

	public const uint EventEnding = 6u;

	private static readonly Logger Log = new Logger("BagRogueService");

	public const uint MikuRelicOptionType = 9u;

	private static List<uint>? _jigsawIds;

	private static Dictionary<uint, List<JigsawGoods>>? _jigsawByQuality;

	private static Dictionary<uint, (uint SubId, uint Star)>? _jigsawMeta;

	private static Dictionary<(uint SubId, uint Star), uint>? _jigsawBySubStar;

	private static Dictionary<uint, uint>? _jigsawRiki;

	private static Dictionary<uint, uint>? _relicRiki;

	private static Dictionary<uint, uint>? _jigsawPrices;

	private static Dictionary<uint, (uint GroupId, uint Min, uint Max)>? _jigsawAffixRule;

	public const uint BagRikiStart = 21u;

	public const uint BagRikiEnd = 30u;

	public const uint RelicRikiStart = 550u;

	public const uint RelicRikiEnd = 570u;

	private static Dictionary<uint, (List<uint> Qualities, int Column)>? _storeCfgs;

	private static Dictionary<uint, List<uint>>? _storeRefreshCosts;

	private static Dictionary<uint, List<(uint AffixId, uint Weight)>>? _affixGroups;

	public const uint MikuRelicPickOptionBase = 8500u;

	public const uint MikuJigsawPickOptionBase = 11000u;

	public const uint RelicSidBase = 501u;

	public const uint RelicSidStride = 3u;

	public const int RelicSlotCount = 3;

	public const uint BagItemSidBase = 31u;

	public const uint BagItemSidStride = 3u;

	public const uint BagItemMaxCount = 100u;

	public static BagRogueScheduleExcel? CurrentSchedule => SelectSchedule(DateTime.Now);

	public static List<BagRogueNodeExcel> NodesOf(uint mapId)
	{
		return (from node in GameData.BagRogueNodeData.Values
			where node.MapId == mapId
			orderby node.Line, node.PositionId
			select node).ToList();
	}

	public static bool IsEndingNode(BagRogueNodeExcel node)
	{
		if (node.EventType == 6)
		{
			return true;
		}
		if (node.EventType != 5)
		{
			return false;
		}
		return !GameData.BagRogueNodeData.Values.Any((BagRogueNodeExcel other) => other.MapId == node.MapId && other.PreNodes.Contains(node.NodeId));
	}

	private static long Unix(string? raw)
	{
		DateTime? dateTime = DlcEraState.ParseConfigTime(raw);
		long result;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			result = new DateTimeOffset(valueOrDefault).ToUnixTimeSeconds();
		}
		else
		{
			result = 0L;
		}
		return result;
	}

	public static BagRogueScheduleExcel? EraSchedule()
	{
		uint num = DlcPin.Override();
		if (DlcPin.IsDefault(num) || !GameData.DlcActivityData.TryGetValue(num, out DlcActivityExcel value))
		{
			return null;
		}
		long eraLo = Unix(value.EnterStartTime);
		long eraHi = Unix(value.CloseEndTime);
		if (eraLo <= 0 || eraHi <= 0)
		{
			return null;
		}
		return GameData.BagRogueScheduleData.Values.OrderBy((BagRogueScheduleExcel row) => row.Id).FirstOrDefault((BagRogueScheduleExcel row) =>
		{
			long num2 = Unix(row.Start);
			long num3 = Unix(row.End);
			return num2 > 0 && num3 > 0 && num2 <= eraHi && num3 >= eraLo;
		});
	}

	public static BagRogueScheduleExcel? SelectSchedule(DateTime now)
	{
		List<BagRogueScheduleExcel> list = GameData.BagRogueScheduleData.Values.OrderBy((BagRogueScheduleExcel row) => row.Id).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		BagRogueScheduleExcel bagRogueScheduleExcel = EraSchedule();
		if (bagRogueScheduleExcel != null)
		{
			return bagRogueScheduleExcel;
		}
		long current = new DateTimeOffset(now).ToUnixTimeSeconds();
		foreach (BagRogueScheduleExcel item in list)
		{
			long num = Unix(item.Start);
			long num2 = Unix(item.End);
			if (num > 0 && num2 > 0 && current >= num && current <= num2)
			{
				return item;
			}
		}
		BagRogueScheduleExcel bagRogueScheduleExcel2 = (from row in list
			where Unix(row.Start) > 0 && Unix(row.Start) <= current
			orderby row.Id descending
			select row).FirstOrDefault();
		BagRogueScheduleExcel bagRogueScheduleExcel3 = bagRogueScheduleExcel2;
		if (bagRogueScheduleExcel3 == null)
		{
			bagRogueScheduleExcel3 = list[list.Count - 1];
		}
		return bagRogueScheduleExcel3;
	}

	public static uint ScheduleId(PlayerInstance player)
	{
		uint value = player.Attributes.GetValue(142u, 1u);
		return (value != 0) ? value : (CurrentSchedule?.Id ?? 0);
	}

	public static BagRogueScheduleExcel? ScheduleOf(PlayerInstance player)
	{
		BagRogueScheduleExcel bagRogueScheduleExcel = EraSchedule();
		if (bagRogueScheduleExcel != null)
		{
			return bagRogueScheduleExcel;
		}
		uint num = ScheduleId(player);
		if (num != 0 && GameData.BagRogueScheduleData.TryGetValue(num, out BagRogueScheduleExcel value))
		{
			return value;
		}
		return CurrentSchedule;
	}

	public static void Seed(PlayerInstance player, NtfSyncPlayer? sync)
	{
		BagRogueScheduleExcel bagRogueScheduleExcel = SelectSchedule(DateTime.Now);
		if (bagRogueScheduleExcel != null && player.Attributes.GetValue(142u, 1u) != bagRogueScheduleExcel.Id)
		{
			PlayerAttr attr = player.Attributes.Set(142u, 1u, bagRogueScheduleExcel.Id);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
	}

	public static uint BattleLevelFor(BagRogueNodeExcel node, BagRogueScheduleExcel? schedule)
	{
		if (GameData.BagRogueLevelData.Count == 0)
		{
			return 0u;
		}
		bool flag = schedule != null && schedule.MapIds.Count > 0 && schedule.MapIds.Max() >= 200;
		uint min = (flag ? 8500u : 8300u);
		uint max = (flag ? 8699u : 8399u);
		List<BagRogueLevelExcel> list = (from level in GameData.BagRogueLevelData.Values
			where level.GroupId == node.LevelGroup && level.Id >= min && level.Id <= max
			orderby level.Id
			select level).ToList();
		if (list.Count == 0)
		{
			list = (from level in GameData.BagRogueLevelData.Values
				where level.GroupId == node.LevelGroup
				orderby level.Id
				select level).ToList();
		}
		if (list.Count == 0)
		{
			Log.Warn($"BagRogue: no stage for level group {node.LevelGroup} (node {node.NodeId}).");
			return 0u;
		}
		int index = Math.Min(list.Count - 1, (int)(node.Line / 4));
		return list[index].Id;
	}

	public static List<uint> OptionsOfGroup(uint groupId)
	{
		List<BagRogueAdventureExcel> source = (from option in GameData.BagRogueAdventureData.Values
			where option.GroupId == groupId
			orderby option.Id
			select option).ToList();
		List<BagRogueAdventureExcel> list = source.Where((BagRogueAdventureExcel option) => !string.IsNullOrEmpty(option.OptionName)).ToList();
		if (list.Count > 0)
		{
			List<BagRogueAdventureExcel> list2 = list.Where((BagRogueAdventureExcel option) => option.BuffId != 0).ToList();
			List<uint> list3 = ((list2.Count <= 0) ? list.Select((BagRogueAdventureExcel option) => option.Id).ToList() : (from option in list2
				group option by option.BuffId into @group
				select @group.OrderBy((BagRogueAdventureExcel option) => option.Id).First().Id).ToList());
			foreach (BagRogueAdventureExcel item in list.Where((BagRogueAdventureExcel option) => option.OptionType == 9))
			{
				if (!list3.Contains(item.Id))
				{
					list3.Add(item.Id);
				}
			}
			return list3.OrderBy((uint id) => id).ToList();
		}
		List<BagRogueAdventureExcel> list4 = source.Where((BagRogueAdventureExcel option) =>
		{
			uint optionType = option.OptionType;
			bool flag = ((optionType == 1 || optionType == 6) ? true : false);
			return flag || option.BuffId == 0;
		}).ToList();
		if (list4.Count > 0)
		{
			return list4.Select((BagRogueAdventureExcel option) => option.Id).ToList();
		}
		return source.Select((BagRogueAdventureExcel option) => option.Id).ToList();
	}

	public static List<uint> NodeOptionIds(uint nodeId, BagRogueNodeExcel node)
	{
		List<uint> list = node.AdventureGroupPool;
		if (list.Count == 0 && node.AdventureGroupId != 0)
		{
			int num = 1;
			List<uint> list2 = new List<uint>(num);
			CollectionsMarshal.SetCount(list2, num);
			CollectionsMarshal.AsSpan(list2)[0] = node.AdventureGroupId;
			list = list2;
		}
		int num2 = ((node.AdventureOptionNum == 0) ? list.Count : ((int)node.AdventureOptionNum));
		if (num2 <= 0)
		{
			num2 = list.Count;
		}
		List<uint> list3 = new List<uint>();
		HashSet<uint> hashSet = new HashSet<uint>();
		for (int i = 0; i < list.Count; i++)
		{
			if (list3.Count >= num2)
			{
				break;
			}
			List<uint> list4 = OptionsOfGroup(list[i]);
			if (list4.Count == 0)
			{
				continue;
			}
			int num3 = (int)((uint)((int)nodeId + i) % (uint)list4.Count);
			uint num4 = 0u;
			for (int j = 0; j < list4.Count; j++)
			{
				uint num5 = list4[(num3 + j) % list4.Count];
				if (hashSet.Add(num5))
				{
					num4 = num5;
					break;
				}
			}
			list3.Add((num4 == 0) ? list4[num3] : num4);
		}
		return list3;
	}

	public static List<uint> NodeOptionIds(PlayerInstance player, uint nodeId, BagRogueNodeExcel node)
	{
		if (JsonNode.Parse(ReadNodeTable(player)) is JsonObject jsonObject && jsonObject[$"N{nodeId}"] is JsonObject jsonObject2 && jsonObject2["D"] is JsonArray jsonArray)
		{
			List<uint> list = new List<uint>();
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonArray { Count: >0 } jsonArray2)
				{
					uint num = ReadUint(jsonArray2[0]);
					if (num != 0)
					{
						list.Add(num);
					}
				}
			}
			if (list.Count > 0)
			{
				return list;
			}
		}
		return NodeOptionIds(nodeId, node);
	}

	public static JsonArray AdventureOptions(uint nodeId, BagRogueNodeExcel node)
	{
		JsonArray jsonArray = new JsonArray();
		uint num = JigsawForNode(nodeId);
		foreach (uint item in NodeOptionIds(nodeId, node))
		{
			InlineArray2<JsonNode> buffer = default;
			buffer[0] = item;
			buffer[1] = num;
			jsonArray.Add(new JsonArray(buffer));
		}
		return jsonArray;
	}

	private static uint JigsawForNode(uint nodeId)
	{
		return 1201u;
	}

	internal static uint JigsawPrice(uint id)
	{
		if (_jigsawPrices == null)
		{
			JigsawByQuality();
		}
		uint value;
		return (_jigsawPrices != null && _jigsawPrices.TryGetValue(id, out value)) ? value : 0u;
	}

	internal static (uint SubId, uint Star)? JigsawMeta(uint tpId)
	{
		if (_jigsawMeta == null)
		{
			JigsawByQuality();
		}
		(uint, uint) value;
		return (_jigsawMeta != null && _jigsawMeta.TryGetValue(tpId, out value)) ? new (uint, uint)?(value) : (((uint, uint)?)null);
	}

	internal static (uint GroupId, uint Min, uint Max)? JigsawAffixRule(uint tpId)
	{
		if (_jigsawAffixRule == null)
		{
			JigsawByQuality();
		}
		(uint, uint, uint) value;
		return (_jigsawAffixRule != null && _jigsawAffixRule.TryGetValue(tpId, out value)) ? new (uint, uint, uint)?(value) : (((uint, uint, uint)?)null);
	}

	internal static List<uint> JigsawPickAffixes(uint pieceId, uint seed)
	{
		List<uint> list = new List<uint>();
		(uint, uint, uint)? tuple = JigsawAffixRule(pieceId);
		if (!tuple.HasValue || tuple.Value.Item1 == 0)
		{
			return list;
		}
		Random random = new Random(((int)seed * -1640531535) & 0x7FFFFFFF);
		uint item = tuple.Value.Item2;
		uint num = tuple.Value.Item3;
		uint num2 = item;
		if (num < num2)
		{
			num = num2;
		}
		uint num3 = ((num2 >= num) ? num2 : (num2 + (uint)(random.NextDouble() * (double)(num - num2 + 1))));
		if (num3 > 4)
		{
			num3 = 4u;
		}
		for (int i = 0; i < num3; i++)
		{
			uint num4 = PickAffix(tuple.Value.Item1, random);
			if (num4 != 0 && !list.Contains(num4))
			{
				list.Add(num4);
			}
		}
		return list;
	}

	internal static void SetRikiFlag(PlayerInstance player, uint rikiId, bool relic, NtfSyncPlayer? sync)
	{
		if (rikiId == 0)
		{
			return;
		}
		uint num = (relic ? 550u : 21u);
		uint num2 = (relic ? 570u : 30u);
		uint num3 = num + rikiId / 30;
		if (num3 <= num2)
		{
			int num4 = (int)(rikiId % 30);
			uint value = player.Attributes.GetValue(142u, num3) | (uint)(1 << num4);
			PlayerAttr attr = player.Attributes.Set(142u, num3, value);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
	}

	internal static void SetJigsawRikiFlag(PlayerInstance player, uint tpId, NtfSyncPlayer? sync)
	{
		if (_jigsawRiki == null)
		{
			JigsawByQuality();
		}
		if (_jigsawRiki != null && _jigsawRiki.TryGetValue(tpId, out var value))
		{
			SetRikiFlag(player, value, relic: false, sync);
		}
	}

	private static Dictionary<uint, uint> RelicRikis()
	{
		if (_relicRiki != null)
		{
			return _relicRiki;
		}
		Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "bag_rogue";
			buffer[4] = "relic.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item?["ID"] != null && uint.TryParse(item["ID"].ToString(), out var result) && result != 0 && uint.TryParse(item["RikiId"]?.ToString(), out var result2) && result2 != 0)
					{
						dictionary[result] = result2;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("relic.json (riki) load failed: " + ex.Message);
		}
		_relicRiki = dictionary;
		return dictionary;
	}

	internal static void SetRelicRikiFlag(PlayerInstance player, uint relicId, NtfSyncPlayer? sync)
	{
		SetRikiFlag(player, RelicRikis().TryGetValue(relicId, out var value) ? value : 0u, relic: true, sync);
	}

	internal static void UnlockAllRiki(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (_jigsawRiki == null)
		{
			JigsawByQuality();
		}
		if (_jigsawRiki != null)
		{
			foreach (uint item in _jigsawRiki.Values.Distinct())
			{
				SetRikiFlag(player, item, relic: false, sync);
			}
		}
		foreach (uint item2 in RelicRikis().Values.Distinct())
		{
			SetRikiFlag(player, item2, relic: true, sync);
		}
	}

	internal static uint? JigsawUpgradeId(uint tpId)
	{
		if (_jigsawMeta == null || _jigsawBySubStar == null)
		{
			JigsawByQuality();
		}
		if (_jigsawMeta != null && _jigsawMeta.TryGetValue(tpId, out (uint, uint) value) && _jigsawBySubStar != null && _jigsawBySubStar.TryGetValue((value.Item1, value.Item2 + 1), out var value2))
		{
			return value2;
		}
		return null;
	}

	private static Dictionary<uint, List<JigsawGoods>> JigsawByQuality()
	{
		if (_jigsawByQuality != null)
		{
			return _jigsawByQuality;
		}
		Dictionary<uint, List<JigsawGoods>> dictionary = new Dictionary<uint, List<JigsawGoods>>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "bag_rogue";
			buffer[4] = "jigsaw.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					JsonNode jsonNode = item?["ID"];
					if (jsonNode == null || !uint.TryParse(jsonNode.ToString(), out var result) || result == 0)
					{
						continue;
					}
					uint num = (uint.TryParse(item["Quality"]?.ToString(), out var result2) ? result2 : 0u);
					uint weight = ((!uint.TryParse(item["Weight"]?.ToString(), out var result3)) ? 1u : result3);
					uint num2 = (uint.TryParse(item["Price"]?.ToString(), out var result4) ? result4 : 0u);
					uint result5 = 0u;
					if (item["AffixRule"] is JsonObject { Count: >0 } jsonObject)
					{
						uint.TryParse(jsonObject.First().Key, out result5);
						if (jsonObject.First().Value is JsonArray { Count: >=2 } jsonArray2 && uint.TryParse(jsonArray2[0]?.ToString(), out var result6) && uint.TryParse(jsonArray2[1]?.ToString(), out var result7))
						{
							if (_jigsawAffixRule == null)
							{
								_jigsawAffixRule = new Dictionary<uint, (uint, uint, uint)>();
							}
							_jigsawAffixRule[result] = (result5, result6, result7);
						}
					}
					if (_jigsawPrices == null)
					{
						_jigsawPrices = new Dictionary<uint, uint>();
					}
					_jigsawPrices[result] = num2;
					uint num3 = (uint.TryParse(item["SubID"]?.ToString(), out var result8) ? result8 : 0u);
					uint num4 = (uint.TryParse(item["Star"]?.ToString(), out var result9) ? result9 : 0u);
					if (num3 != 0 && num4 != 0)
					{
						if (_jigsawMeta == null)
						{
							_jigsawMeta = new Dictionary<uint, (uint, uint)>();
						}
						if (_jigsawBySubStar == null)
						{
							_jigsawBySubStar = new Dictionary<(uint, uint), uint>();
						}
						_jigsawMeta[result] = (num3, num4);
						_jigsawBySubStar.TryAdd((num3, num4), result);
					}
					if (uint.TryParse(item["RikiId"]?.ToString(), out var result10) && result10 != 0)
					{
						if (_jigsawRiki == null)
						{
							_jigsawRiki = new Dictionary<uint, uint>();
						}
						_jigsawRiki[result] = result10;
					}
					if (!dictionary.TryGetValue(num, out var value))
					{
						value = (dictionary[num] = new List<JigsawGoods>());
					}
					value.Add(new JigsawGoods(result, num, weight, result5, num2));
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("jigsaw.json (shop shelf) load failed: " + ex.Message);
		}
		_jigsawByQuality = dictionary;
		return dictionary;
	}

	private static Dictionary<uint, (List<uint> Qualities, int Column)> StoreConfigs()
	{
		if (_storeCfgs != null)
		{
			return _storeCfgs;
		}
		Dictionary<uint, (List<uint>, int)> dictionary = new Dictionary<uint, (List<uint>, int)>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "bag_rogue";
			buffer[4] = "store.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item2 in jsonArray)
				{
					JsonNode jsonNode = item2?["Id"];
					if (jsonNode == null || !uint.TryParse(jsonNode.ToString(), out var result) || result == 0)
					{
						continue;
					}
					List<uint> list = new List<uint>();
					if (item2["JigsawGroup"] is JsonArray jsonArray2)
					{
						foreach (JsonNode item3 in jsonArray2)
						{
							if (uint.TryParse(item3?.ToString(), out var result2) && result2 != 0)
							{
								list.Add(result2);
							}
						}
					}
					int item = (int.TryParse(item2["Column"]?.ToString(), out var result3) ? result3 : 3);
					dictionary[result] = (list, item);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("store.json load failed: " + ex.Message);
		}
		_storeCfgs = dictionary;
		return dictionary;
	}

	internal static List<uint> StoreRefreshCosts(uint storeId)
	{
		if (_storeRefreshCosts == null)
		{
			Dictionary<uint, List<uint>> dictionary = new Dictionary<uint, List<uint>>();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "dlc";
				buffer[3] = "bag_rogue";
				buffer[4] = "store.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
				{
					foreach (JsonNode item in jsonArray)
					{
						JsonNode jsonNode = item?["Id"];
						if (jsonNode == null || !uint.TryParse(jsonNode.ToString(), out var result) || result == 0)
						{
							continue;
						}
						List<uint> list = new List<uint>();
						if (item["RefreshCost"] is JsonArray jsonArray2)
						{
							foreach (JsonNode item2 in jsonArray2)
							{
								if (uint.TryParse(item2?.ToString(), out var result2) && result2 != 0)
								{
									list.Add(result2);
								}
							}
						}
						dictionary[result] = list;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warn("store.json RefreshCost load failed: " + ex.Message);
			}
			_storeRefreshCosts = dictionary;
		}
		List<uint> value;
		return _storeRefreshCosts.TryGetValue(storeId, out value) ? value : new List<uint>();
	}

	private static Dictionary<uint, List<(uint AffixId, uint Weight)>> AffixGroups()
	{
		if (_affixGroups != null)
		{
			return _affixGroups;
		}
		Dictionary<uint, List<(uint, uint)>> dictionary = new Dictionary<uint, List<(uint, uint)>>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "bag_rogue";
			buffer[4] = "affixgroup.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item2 in jsonArray)
				{
					if (item2 is JsonObject jsonObject && uint.TryParse(jsonObject["GroupID"]?.ToString(), out var result) && result != 0 && uint.TryParse(jsonObject["AffixID"]?.ToString(), out var result2) && result2 != 0)
					{
						uint item = ((!uint.TryParse(jsonObject["Weight"]?.ToString(), out var result3)) ? 1u : result3);
						if (!dictionary.TryGetValue(result, out var value))
						{
							value = (dictionary[result] = new List<(uint, uint)>());
						}
						value.Add((result2, item));
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("affixgroup.json load failed: " + ex.Message);
		}
		_affixGroups = dictionary;
		return dictionary;
	}

	private static uint PickAffix(uint groupId, Random rng)
	{
		if (groupId == 0 || !AffixGroups().TryGetValue(groupId, out List<(uint, uint)> value) || value.Count == 0)
		{
			return 0u;
		}
		long num = 0L;
		foreach (var item4 in value)
		{
			uint item = item4.Item2;
			num += item;
		}
		long num2 = (long)(rng.NextDouble() * (double)num);
		foreach (var item5 in value)
		{
			uint item2 = item5.Item1;
			uint item3 = item5.Item2;
			num2 -= item3;
			if (num2 < 0)
			{
				return item2;
			}
		}
		List<(uint, uint)> list = value;
		return list[list.Count - 1].Item1;
	}

	internal static List<(JigsawGoods Goods, uint AffixId)> StoreShelf(uint storeId, uint seed)
	{
		List<(JigsawGoods, uint)> list = new List<(JigsawGoods, uint)>();
		StoreConfigs().TryGetValue(storeId, out (List<uint>, int) value);
		int num = ((value.Item2 > 0) ? value.Item2 : 3);
		List<JigsawGoods> list2 = new List<JigsawGoods>();
		Dictionary<uint, List<JigsawGoods>> dictionary = JigsawByQuality();
		if (value.Item1.Count > 0)
		{
			foreach (uint item in value.Item1)
			{
				if (dictionary.TryGetValue(item, out var value2))
				{
					list2.AddRange(value2);
				}
			}
		}
		if (list2.Count == 0)
		{
			foreach (KeyValuePair<uint, List<JigsawGoods>> item2 in dictionary)
			{
				list2.AddRange(item2.Value);
			}
		}
		if (list2.Count == 0)
		{
			return list;
		}
		Random random = new Random(((int)seed * -1640531535) & 0x7FFFFFFF);
		List<JigsawGoods> list3 = new List<JigsawGoods>(list2);
		for (int i = 0; i < num; i++)
		{
			if (list3.Count <= 0)
			{
				break;
			}
			long num2 = 0L;
			foreach (JigsawGoods item3 in list3)
			{
				num2 += item3.Weight;
			}
			long num3 = (long)(random.NextDouble() * (double)num2);
			JigsawGoods jigsawGoods = list3[list3.Count - 1];
			foreach (JigsawGoods item4 in list3)
			{
				num3 -= item4.Weight;
				if (num3 < 0)
				{
					jigsawGoods = item4;
					break;
				}
			}
			list.Add((jigsawGoods, PickAffix(jigsawGoods.AffixGroup, random)));
			list3.Remove(jigsawGoods);
		}
		return list;
	}

	private static List<uint> JigsawIds()
	{
		if (_jigsawIds != null)
		{
			return _jigsawIds;
		}
		_jigsawIds = new List<uint>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "bag_rogue";
			buffer[4] = "jigsaw.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return _jigsawIds;
			}
			if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray))
			{
				return _jigsawIds;
			}
			foreach (JsonNode item in jsonArray)
			{
				JsonNode jsonNode = item?["ID"];
				if (jsonNode != null && uint.TryParse(jsonNode.ToString(), out var result) && result != 0)
				{
					_jigsawIds.Add(result);
				}
			}
			_jigsawIds.Sort();
		}
		catch (Exception ex)
		{
			Log.Warn("jigsaw.json load failed: " + ex.Message);
		}
		return _jigsawIds;
	}

	public static uint StoryForMap(uint mapId)
	{
		if (GameData.BagRogueStoryData.Count == 0)
		{
			return 0u;
		}
		if (1 == 0)
		{
		}
		uint num;
		switch (mapId)
		{
		case 211u:
			num = 5u;
			break;
		case 212u:
			num = 6u;
			break;
		case 213u:
			num = 7u;
			break;
		case 221u:
			num = 8u;
			break;
		case 222u:
			num = 9u;
			break;
		case 231u:
		case 232u:
			num = 10u;
			break;
		default:
			num = mapId;
			break;
		}
		if (1 == 0)
		{
		}
		uint value = num;
		return Math.Clamp(value, 1u, GameData.BagRogueStoryData.Values.Max((BagRogueStoryExcel row) => row.Id));
	}

	public static uint EndingTypeFor(BagRogueNodeExcel node)
	{
		if (GameData.BagRogueStoryData.Count == 0)
		{
			return 0u;
		}
		uint key = StoryForMap(node.MapId);
		BagRogueStoryExcel value;
		return GameData.BagRogueStoryData.TryGetValue(key, out value) ? value.EndingType : 0u;
	}

	public static JsonObject BuildNodeTable(PlayerInstance player, BagRogueScheduleExcel schedule, uint mapId)
	{
		JsonObject jsonObject = new JsonObject();
		if (mapId == 0)
		{
			return jsonObject;
		}
		foreach (BagRogueNodeExcel item in NodesOf(mapId))
		{
			JsonObject jsonObject2 = new JsonObject
			{
				["T"] = item.EventType,
				["C"] = 0
			};
			switch (item.EventType)
			{
			case 1u:
			{
				uint num2 = BattleLevelFor(item, schedule);
				if (num2 != 0)
				{
					jsonObject2["D"] = new JsonArray((JsonNode)num2);
				}
				break;
			}
			case 4u:
			{
				uint num5 = BattleLevelFor(item, schedule);
				if (num5 != 0)
				{
					jsonObject2["T"] = 1u;
					jsonObject2["D"] = new JsonArray((JsonNode)num5);
				}
				break;
			}
			case 5u:
			{
				uint num6 = StoryForMap(item.MapId);
				if (num6 != 0)
				{
					jsonObject2["D"] = new JsonArray((JsonNode)num6);
				}
				break;
			}
			case 2u:
			{
				JsonArray jsonArray2 = AdventureOptions(item.NodeId, item);
				if (jsonArray2.Count > 0)
				{
					jsonObject2["D"] = jsonArray2;
				}
				break;
			}
			case 3u:
			{
				uint num3 = ((item.StoreId == 0) ? 1u : item.StoreId);
				JsonArray jsonArray = new JsonArray((JsonNode)num3);
				JsonObject jsonObject3 = new JsonObject();
				foreach (var (jigsawGoods, num4) in StoreShelf(num3, item.NodeId))
				{
					jsonArray.Add(jigsawGoods.Id);
					jsonObject3[$"J{jigsawGoods.Id}"] = ((num4 != 0) ? new JsonArray((JsonNode)num4) : new JsonArray());
				}
				jsonObject2["D"] = jsonArray;
				jsonObject2["JA"] = jsonObject3;
				jsonObject2["S"] = 0;
				break;
			}
			case 6u:
			{
				uint num = EndingTypeFor(item);
				if (num != 0)
				{
					jsonObject2["S"] = num;
				}
				break;
			}
			}
			jsonObject[$"N{item.NodeId}"] = jsonObject2;
		}
		return jsonObject;
	}

	public static void StartRoute(PlayerInstance player, BagRogueScheduleExcel schedule, NtfSyncPlayer sync)
	{
		List<uint> list;
		if (schedule.MapIds.Count <= 0)
		{
			int num = 1;
			list = new List<uint>(num);
			CollectionsMarshal.SetCount(list, num);
			CollectionsMarshal.AsSpan(list)[0] = 1u;
		}
		else
		{
			list = schedule.MapIds;
		}
		List<uint> list2 = list;
		int num2 = ClearCount(player);
		uint num3 = list2[(int)(num2 % (uint)list2.Count)];
		PlayerAttr attr = player.Attributes.Set(142u, 4u, num3);
		player.Attributes.SyncTo(sync, attr);
		PlayerAttr attr2 = player.Attributes.Set(142u, 6u, 0u);
		player.Attributes.SyncTo(sync, attr2);
		PlayerAttr attr3 = player.Attributes.Set(142u, 3u, (schedule.BagVolume == 0) ? 20u : schedule.BagVolume);
		player.Attributes.SyncTo(sync, attr3);
		PlayerAttr attr4 = player.Attributes.Set(142u, 7u, 0u);
		player.Attributes.SyncTo(sync, attr4);
		PlayerAttr attr5 = player.Attributes.Set(142u, 20u, 0u);
		player.Attributes.SyncTo(sync, attr5);
		ResetBagItems(player, sync);
		UnlockAllRiki(player, sync);
		PlayerStrAttr attr6 = player.Attributes.SetString(61u, 1u, BuildNodeTable(player, schedule, num3).ToJsonString());
		player.Attributes.SyncTo(sync, attr6);
		PlayerStrAttr attr7 = player.Attributes.SetString(61u, 2u, "{}");
		player.Attributes.SyncTo(sync, attr7);
		PlayerStrAttr attr8 = player.Attributes.SetString(61u, 3u, "[]");
		player.Attributes.SyncTo(sync, attr8);
		RecordOpen(player, sync);
		GrantStartRelics(player, sync);
		Log.Info($"BagRogue route: period {schedule.Id}, map {num3}, {NodesOf(num3).Count} node(s) for uid={player.Uid}.");
	}

	public static bool TryWalk(PlayerInstance player, uint nodeId, NtfSyncPlayer sync, out string error)
	{
		error = string.Empty;
		uint value = player.Attributes.GetValue(142u, 4u);
		if (value == 0)
		{
			error = "ui.TxtChapterUnfinish";
			return false;
		}
		if (!GameData.BagRogueNodeData.TryGetValue(nodeId, out BagRogueNodeExcel value2) || value2.MapId != value)
		{
			error = "error.BadParam";
			return false;
		}
		JsonObject jsonObject = (JsonNode.Parse(ReadNodeTable(player)) as JsonObject) ?? new JsonObject();
		if (!(jsonObject[$"N{nodeId}"] is JsonObject jsonObject2))
		{
			error = "bagrogue.massage_NodeLock";
			return false;
		}
		if (ReadUint(jsonObject2["C"]) != 0)
		{
			error = "bagrogue.massage_NodeExecuted";
			return false;
		}
		bool flag = value2.PreNodes.Count == 0;
		foreach (uint preNode in value2.PreNodes)
		{
			if (jsonObject[$"N{preNode}"] is JsonObject jsonObject3 && ReadUint(jsonObject3["C"]) != 0)
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			error = "bagrogue.massage_NodeLock";
			return false;
		}
		uint value3 = player.Attributes.GetValue(142u, 6u);
		if (value3 != 0 && value3 != nodeId && jsonObject[$"N{value3}"] is JsonObject jsonObject4)
		{
			jsonObject4["C"] = 1;
		}
		if (value2.EventType == 3 && ReadUint(jsonObject2["C"]) == 0)
		{
			jsonObject2["C"] = 1;
		}
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync, attr);
		PlayerAttr attr2 = player.Attributes.Set(142u, 6u, nodeId);
		player.Attributes.SyncTo(sync, attr2);
		return true;
	}

	public static string ReadNodeTable(PlayerInstance player)
	{
		return player.Attributes.GetStringValue(61u, 1u) ?? "{}";
	}

	public static bool AlignEraMap(PlayerInstance player, BagRogueScheduleExcel schedule, NtfSyncPlayer? sync)
	{
		uint value = player.Attributes.GetValue(142u, 4u);
		if (value == 0)
		{
			return false;
		}
		List<uint> list;
		if (schedule.MapIds.Count <= 0)
		{
			int num = 1;
			list = new List<uint>(num);
			CollectionsMarshal.SetCount(list, num);
			CollectionsMarshal.AsSpan(list)[0] = 1u;
		}
		else
		{
			list = schedule.MapIds;
		}
		List<uint> list2 = list;
		if (list2.Contains(value))
		{
			return false;
		}
		NtfSyncPlayer sync2 = sync ?? new NtfSyncPlayer();
		int value2 = 0;
		if (JsonNode.Parse(player.Attributes.GetStringValue(61u, 3u) ?? "[]") is JsonArray jsonArray)
		{
			value2 = jsonArray.Count;
		}
		FinishRoute(player, sync2);
		StartRoute(player, schedule, sync2);
		Log.Warn($"BagRogue: map {value} belongs to another period, rebuilt on period {schedule.Id} (map {player.Attributes.GetValue(142u, 4u)}, {value2} pending reward row(s) dropped) for uid={player.Uid}.");
		return true;
	}

	public static bool RepairAdventureNodes(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player.Attributes.GetValue(142u, 4u) == 0)
		{
			return false;
		}
		if (!(JsonNode.Parse(ReadNodeTable(player)) is JsonObject { Count: not 0 } jsonObject))
		{
			return false;
		}
		bool flag = false;
		foreach (var (text2, jsonNode2) in jsonObject)
		{
			if (jsonNode2 is JsonObject jsonObject2 && ReadUint(jsonObject2["T"]) == 2 && text2.Length >= 2 && uint.TryParse(text2.Substring(1), out var result) && GameData.BagRogueNodeData.TryGetValue(result, out BagRogueNodeExcel value) && value.AdventureGroupId != 0)
			{
				JsonArray jsonArray = AdventureOptions(result, value);
				if (jsonArray.Count != 0 && !(jsonObject2["D"]?.ToJsonString() == jsonArray.ToJsonString()))
				{
					jsonObject2["D"] = jsonArray;
					flag = true;
				}
			}
		}
		if (!flag)
		{
			return false;
		}
		NtfSyncPlayer sync2 = sync ?? new NtfSyncPlayer();
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync2, attr);
		Log.Info($"BagRogue: repaired legacy adventure option layout in node table for uid={player.Uid}.");
		return true;
	}

	public static bool RepairNodeTable(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint value = player.Attributes.GetValue(142u, 4u);
		if (value == 0)
		{
			return false;
		}
		BagRogueScheduleExcel bagRogueScheduleExcel = ScheduleOf(player);
		if (bagRogueScheduleExcel == null)
		{
			return false;
		}
		JsonObject jsonObject = (JsonNode.Parse(ReadNodeTable(player)) as JsonObject) ?? new JsonObject();
		JsonObject jsonObject2 = BuildNodeTable(player, bagRogueScheduleExcel, value);
		bool flag = false;
		foreach (var (propertyName, jsonNode2) in jsonObject2)
		{
			if (!(jsonNode2 is JsonObject jsonObject3))
			{
				continue;
			}
			if (!(jsonObject[propertyName] is JsonObject jsonObject4))
			{
				jsonObject[propertyName] = jsonObject3.DeepClone();
				flag = true;
				continue;
			}
			if (ReadUint(jsonObject4["T"]) == 0 && ReadUint(jsonObject3["T"]) != 0)
			{
				jsonObject4["T"] = ReadUint(jsonObject3["T"]);
				flag = true;
			}
			if (jsonObject4["D"] == null)
			{
				JsonNode jsonNode3 = jsonObject3["D"];
				if (jsonNode3 != null)
				{
					jsonObject4["D"] = jsonNode3.DeepClone();
					flag = true;
					goto IL_026d;
				}
			}
			if (ReadUint(jsonObject3["T"]) == 3 && jsonObject3["D"] != null && jsonObject4["JA"] == null)
			{
				JsonNode jsonNode4 = jsonObject3["D"];
				if (jsonNode4 != null)
				{
					jsonObject4["D"] = jsonNode4.DeepClone();
				}
				JsonNode jsonNode5 = jsonObject3["JA"];
				if (jsonNode5 != null)
				{
					jsonObject4["JA"] = jsonNode5.DeepClone();
				}
				JsonNode jsonNode6 = jsonObject3["S"];
				if (jsonNode6 != null)
				{
					jsonObject4["S"] = jsonNode6.DeepClone();
				}
				flag = true;
			}
			goto IL_026d;
			IL_026d:
			if (jsonObject4["S"] == null)
			{
				JsonNode jsonNode7 = jsonObject3["S"];
				if (jsonNode7 != null)
				{
					jsonObject4["S"] = jsonNode7.DeepClone();
					flag = true;
				}
			}
		}
		if (!flag)
		{
			return false;
		}
		NtfSyncPlayer sync2 = sync ?? new NtfSyncPlayer();
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync2, attr);
		Log.Info($"BagRogue: rebuilt missing node fields in 61/1 (map {value}, {jsonObject.Count} node(s)) for uid={player.Uid}.");
		return true;
	}

	public static bool MarkCurrentNodeDone(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint value = player.Attributes.GetValue(142u, 6u);
		if (value == 0)
		{
			return false;
		}
		if (!(JsonNode.Parse(ReadNodeTable(player)) is JsonObject jsonObject))
		{
			return false;
		}
		if (!(jsonObject[$"N{value}"] is JsonObject jsonObject2) || ReadUint(jsonObject2["C"]) != 0)
		{
			return false;
		}
		jsonObject2["C"] = 1;
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		return true;
	}

	public static int ClearCount(PlayerInstance player)
	{
		return (int)((player.Attributes.GetValue(142u, 2u) >> 16) & 0xFFFF);
	}

	public static List<uint> AvailableRelicCandidates(PlayerInstance player)
	{
		HashSet<uint> equipped = EquippedRelics(player).ToHashSet();
		return (from id in UnlockedRelics(player)
			where !equipped.Contains(id)
			orderby id
			select id).ToList();
	}

	public static bool TrySetupRelicPick(PlayerInstance player, NtfSyncPlayer? sync, out List<uint> picks)
	{
		picks = new List<uint>();
		BagRogueNodeExcel bagRogueNodeExcel = CurrentNode(player);
		if (bagRogueNodeExcel == null)
		{
			return false;
		}
		uint value = player.Attributes.GetValue(142u, 6u);
		if (value == 0)
		{
			return false;
		}
		bool hasBattleRelicReward = bagRogueNodeExcel.HasBattleRelicReward;
		bool flag = !hasBattleRelicReward && bagRogueNodeExcel.HasBattleJigsawReward;
		if (!hasBattleRelicReward && !flag)
		{
			return false;
		}
		if (!(JsonNode.Parse(ReadNodeTable(player)) is JsonObject jsonObject))
		{
			return false;
		}
		if (!(jsonObject[$"N{value}"] is JsonObject jsonObject2))
		{
			return false;
		}
		if (ReadUint(jsonObject2["T"]) == 2 && jsonObject2["D"] is JsonArray { Count: >0 } jsonArray)
		{
			foreach (JsonNode item2 in jsonArray)
			{
				if (item2 is JsonArray { Count: >0 } jsonArray2)
				{
					uint num = ReadUint(jsonArray2[0]);
					if (num >= 9501 && num <= 9599)
					{
						picks.Add(num - 8500);
					}
					else if (num >= 12001 && num <= 13999)
					{
						picks.Add(num - 11000);
					}
				}
			}
			if (picks.Count > 0)
			{
				return true;
			}
			picks = new List<uint>();
		}
		List<uint> list;
		if (hasBattleRelicReward)
		{
			list = AvailableRelicCandidates(player);
		}
		else
		{
			HashSet<uint> hashSet = new HashSet<uint>();
			List<uint> list2 = new List<uint>();
			foreach (uint item3 in JigsawIds())
			{
				uint item = JigsawMeta(item3)?.SubId ?? item3;
				if (hashSet.Add(item))
				{
					list2.Add(item3);
				}
			}
			list = list2;
		}
		if (list.Count == 0)
		{
			Log.Info($"BagRogue pick skipped at node {value}: no candidates (mode={(hasBattleRelicReward ? "relic" : "jigsaw")}, unlocked={UnlockedRelics(player).Count}, equipped={EquippedRelics(player).Count}) uid={player.Uid}.");
			return false;
		}
		int num2 = (int)(value % (uint)list.Count);
		HashSet<uint> hashSet2 = new HashSet<uint>();
		for (int i = 0; i < 3; i++)
		{
			if (hashSet2.Count >= list.Count)
			{
				break;
			}
			hashSet2.Add(list[(num2 + i) % list.Count]);
		}
		picks = hashSet2.OrderBy((uint id) => id).ToList();
		JsonArray jsonArray3 = new JsonArray();
		JsonObject jsonObject3 = null;
		if (hasBattleRelicReward)
		{
			foreach (uint pick in picks)
			{
				InlineArray2<JsonNode> buffer = default;
				buffer[0] = 8500 + pick;
				buffer[1] = JigsawForNode(value);
				jsonArray3.Add(new JsonArray(buffer));
			}
		}
		else
		{
			jsonObject3 = new JsonObject();
			foreach (uint pick2 in picks)
			{
				List<uint> list3 = JigsawPickAffixes(pick2, value * 1000003 + pick2);
				JsonArray jsonArray4 = new JsonArray();
				foreach (uint item4 in list3)
				{
					jsonArray4.Add(item4);
				}
				jsonObject3[$"J{pick2}"] = jsonArray4;
				InlineArray2<JsonNode> buffer2 = default;
				buffer2[0] = 11000 + pick2;
				buffer2[1] = pick2;
				jsonArray3.Add(new JsonArray(buffer2));
			}
		}
		jsonObject2["T"] = 2u;
		jsonObject2["D"] = jsonArray3;
		if (jsonObject3 != null)
		{
			jsonObject2["JA"] = jsonObject3;
		}
		PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
		Log.Info($"BagRogue relic pick staged at node {value}: [{string.Join(',', picks)}] uid={player.Uid}.");
		return true;
	}

	public static bool RestoreNodeStandard(PlayerInstance player, NtfSyncPlayer? sync)
	{
		uint value = player.Attributes.GetValue(142u, 6u);
		if (value != 0)
		{
			BagRogueNodeExcel bagRogueNodeExcel = CurrentNode(player);
			if (bagRogueNodeExcel != null)
			{
				if (!(JsonNode.Parse(ReadNodeTable(player)) is JsonObject jsonObject))
				{
					return false;
				}
				if (!(jsonObject[$"N{value}"] is JsonObject jsonObject2))
				{
					return false;
				}
				BagRogueScheduleExcel bagRogueScheduleExcel = ScheduleOf(player);
				if (bagRogueScheduleExcel == null)
				{
					return false;
				}
				if (!(JsonNode.Parse(BuildNodeTable(player, bagRogueScheduleExcel, bagRogueNodeExcel.MapId).ToJsonString()) is JsonObject jsonObject3) || !(jsonObject3[$"N{value}"] is JsonObject jsonObject4))
				{
					return false;
				}
				jsonObject2["T"] = jsonObject4["T"]?.DeepClone();
				jsonObject2["D"] = jsonObject4["D"]?.DeepClone();
				jsonObject2["S"] = jsonObject4["S"]?.DeepClone();
				if (ReadUint(jsonObject2["C"]) == 0)
				{
					jsonObject2["C"] = 1;
				}
				PlayerStrAttr attr = player.Attributes.SetString(61u, 1u, jsonObject.ToJsonString());
				if (sync != null)
				{
					player.Attributes.SyncTo(sync, attr);
				}
				return true;
			}
		}
		return false;
	}

	public static void RecordOpen(PlayerInstance player, NtfSyncPlayer sync)
	{
		BumpCounter(player, sync, clear: false);
	}

	public static void RecordClear(PlayerInstance player, NtfSyncPlayer sync)
	{
		BumpCounter(player, sync, clear: true);
	}

	private static void BumpCounter(PlayerInstance player, NtfSyncPlayer sync, bool clear)
	{
		uint num = player.Attributes.Get(142u, 2u)?.Val ?? 0;
		uint num2 = num & 0xFFFF;
		uint num3 = (num >> 16) & 0xFFFF;
		if (clear)
		{
			num3 = Math.Min(65535u, num3 + 1);
		}
		else
		{
			num2 = Math.Min(65535u, num2 + 1);
		}
		PlayerAttr attr = player.Attributes.Set(142u, 2u, (num3 << 16) | num2);
		player.Attributes.SyncTo(sync, attr);
	}

	public static uint RelicSid(int slot)
	{
		return (uint)(501 + slot * 3);
	}

	public static uint BagItemSid(uint unique)
	{
		return 31 + (unique - 1) * 3;
	}

	internal static void AddBagItem(PlayerInstance player, uint tpId, IReadOnlyList<uint> affixes, NtfSyncPlayer sync)
	{
		uint num = player.Attributes.GetValue(142u, 5u) + 1;
		if (num > 100)
		{
			Log.Warn($"BagRogue AddBagItem: bag full ({100u}), drop tpId {tpId} uid={player.Uid}.");
			return;
		}
		uint num2 = ((affixes.Count > 0) ? affixes[0] : 0u);
		uint num3 = ((affixes.Count > 1) ? affixes[1] : 0u);
		uint num4 = ((affixes.Count > 2) ? affixes[2] : 0u);
		uint num5 = ((affixes.Count > 3) ? affixes[3] : 0u);
		uint num6 = BagItemSid(num);
		PlayerAttr attr = player.Attributes.Set(142u, num6, (num << 16) | tpId);
		player.Attributes.SyncTo(sync, attr);
		PlayerAttr attr2 = player.Attributes.Set(142u, num6 + 1, (num2 << 16) | num3);
		player.Attributes.SyncTo(sync, attr2);
		PlayerAttr attr3 = player.Attributes.Set(142u, num6 + 2, (num4 << 16) | num5);
		player.Attributes.SyncTo(sync, attr3);
		PlayerAttr attr4 = player.Attributes.Set(142u, 5u, num);
		player.Attributes.SyncTo(sync, attr4);
		SetJigsawRikiFlag(player, tpId, sync);
		Log.Info($"BagRogue AddBagItem: slot {num} tpId {tpId} affixes [{num2},{num3},{num4},{num5}] uid={player.Uid}.");
	}

	internal static void ResetBagItems(PlayerInstance player, NtfSyncPlayer sync)
	{
		uint num = player.Attributes.GetValue(142u, 5u);
		PlayerAttr attr = player.Attributes.Set(142u, 5u, 0u);
		player.Attributes.SyncTo(sync, attr);
		switch (num)
		{
		case 0u:
			return;
		default:
			num = 100u;
			break;
		case 1u:
		case 2u:
		case 3u:
		case 4u:
		case 5u:
		case 6u:
		case 7u:
		case 8u:
		case 9u:
		case 10u:
		case 11u:
		case 12u:
		case 13u:
		case 14u:
		case 15u:
		case 16u:
		case 17u:
		case 18u:
		case 19u:
		case 20u:
		case 21u:
		case 22u:
		case 23u:
		case 24u:
		case 25u:
		case 26u:
		case 27u:
		case 28u:
		case 29u:
		case 30u:
		case 31u:
		case 32u:
		case 33u:
		case 34u:
		case 35u:
		case 36u:
		case 37u:
		case 38u:
		case 39u:
		case 40u:
		case 41u:
		case 42u:
		case 43u:
		case 44u:
		case 45u:
		case 46u:
		case 47u:
		case 48u:
		case 49u:
		case 50u:
		case 51u:
		case 52u:
		case 53u:
		case 54u:
		case 55u:
		case 56u:
		case 57u:
		case 58u:
		case 59u:
		case 60u:
		case 61u:
		case 62u:
		case 63u:
		case 64u:
		case 65u:
		case 66u:
		case 67u:
		case 68u:
		case 69u:
		case 70u:
		case 71u:
		case 72u:
		case 73u:
		case 74u:
		case 75u:
		case 76u:
		case 77u:
		case 78u:
		case 79u:
		case 80u:
		case 81u:
		case 82u:
		case 83u:
		case 84u:
		case 85u:
		case 86u:
		case 87u:
		case 88u:
		case 89u:
		case 90u:
		case 91u:
		case 92u:
		case 93u:
		case 94u:
		case 95u:
		case 96u:
		case 97u:
		case 98u:
		case 99u:
		case 100u:
			break;
		}
		for (uint num2 = 1u; num2 <= num; num2++)
		{
			uint num3 = BagItemSid(num2);
			PlayerAttr attr2 = player.Attributes.Set(142u, num3, 0u);
			player.Attributes.SyncTo(sync, attr2);
			PlayerAttr attr3 = player.Attributes.Set(142u, num3 + 1, 0u);
			player.Attributes.SyncTo(sync, attr3);
			PlayerAttr attr4 = player.Attributes.Set(142u, num3 + 2, 0u);
			player.Attributes.SyncTo(sync, attr4);
		}
	}

	public static List<uint> UnlockedRelics(PlayerInstance player)
	{
		BagRogueScheduleExcel bagRogueScheduleExcel = ScheduleOf(player);
		if (bagRogueScheduleExcel == null)
		{
			return new List<uint>();
		}
		List<List<uint>> relicUnlock = bagRogueScheduleExcel.RelicUnlock;
		int num = ClearCount(player);
		List<uint> list = new List<uint>();
		for (int i = 0; i < relicUnlock.Count && i < num; i++)
		{
			foreach (uint item in relicUnlock[i])
			{
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	public static List<uint> EquippedRelics(PlayerInstance player)
	{
		List<uint> list = new List<uint>();
		for (int i = 0; i < 3; i++)
		{
			uint value = player.Attributes.GetValue(142u, RelicSid(i));
			uint num = value & 0xFFFF;
			if (num != 0)
			{
				list.Add(num);
			}
		}
		return list;
	}

	public static void ClearRelics(PlayerInstance player, NtfSyncPlayer? sync)
	{
		for (int i = 0; i < 3; i++)
		{
			if (player.Attributes.GetValue(142u, RelicSid(i)) != 0)
			{
				PlayerAttr attr = player.Attributes.Set(142u, RelicSid(i), 0u);
				if (sync != null)
				{
					player.Attributes.SyncTo(sync, attr);
				}
			}
		}
	}

	public static List<uint> GrantStartRelics(PlayerInstance player, NtfSyncPlayer? sync)
	{
		ClearRelics(player, sync);
		List<uint> list = UnlockedRelics(player);
		if (list.Count == 0)
		{
			return new List<uint>();
		}
		uint num = player.Attributes.GetValue(142u, 2u) & 0xFFFF;
		List<uint> list2 = new List<uint>();
		for (int i = 0; i < 3; i++)
		{
			if (list2.Count >= list.Count)
			{
				break;
			}
			int num2 = (int)((uint)(player.Uid + (int)num + i * 7) % (uint)list.Count);
			for (int j = 0; j < list.Count; j++)
			{
				uint item = list[(num2 + j) % list.Count];
				if (!list2.Contains(item))
				{
					list2.Add(item);
					break;
				}
			}
		}
		for (int k = 0; k < list2.Count; k++)
		{
			uint value = (uint)(k + 1 << 16) | list2[k];
			PlayerAttr attr = player.Attributes.Set(142u, RelicSid(k), value);
			if (sync != null)
			{
				player.Attributes.SyncTo(sync, attr);
			}
		}
		foreach (uint item2 in list2)
		{
			SetRelicRikiFlag(player, item2, sync);
		}
		Log.Info($"BagRogue relics: {string.Join(",", list2)} from {list.Count} unlocked (clears={ClearCount(player)}) uid={player.Uid}.");
		return list2;
	}

	public static bool EnsureRelics(PlayerInstance player, NtfSyncPlayer? sync)
	{
		if (player.Attributes.GetValue(142u, 4u) == 0)
		{
			return false;
		}
		if (UnlockedRelics(player).Count == 0)
		{
			return false;
		}
		if (EquippedRelics(player).Count > 0)
		{
			return false;
		}
		return GrantStartRelics(player, sync).Count > 0;
	}

	public static JsonArray FinishRoute(PlayerInstance player, NtfSyncPlayer sync)
	{
		string stringValue = player.Attributes.GetStringValue(61u, 3u);
		JsonArray jsonArray = new JsonArray();
		if (JsonNode.Parse(stringValue ?? "[]") is JsonArray jsonArray2)
		{
			foreach (JsonNode item in jsonArray2)
			{
				if (!(item is JsonArray jsonArray3))
				{
					continue;
				}
				JsonArray jsonArray4 = new JsonArray();
				foreach (JsonNode item2 in jsonArray3)
				{
					jsonArray4.Add(item2?.DeepClone());
				}
				jsonArray.Add(jsonArray4);
			}
		}
		PlayerAttr attr = player.Attributes.Set(142u, 4u, 0u);
		player.Attributes.SyncTo(sync, attr);
		PlayerAttr attr2 = player.Attributes.Set(142u, 6u, 0u);
		player.Attributes.SyncTo(sync, attr2);
		PlayerAttr attr3 = player.Attributes.Set(142u, 7u, 0u);
		player.Attributes.SyncTo(sync, attr3);
		PlayerAttr attr4 = player.Attributes.Set(142u, 20u, 0u);
		player.Attributes.SyncTo(sync, attr4);
		PlayerStrAttr attr5 = player.Attributes.SetString(61u, 1u, "{}");
		player.Attributes.SyncTo(sync, attr5);
		PlayerStrAttr attr6 = player.Attributes.SetString(61u, 2u, "{}");
		player.Attributes.SyncTo(sync, attr6);
		PlayerStrAttr attr7 = player.Attributes.SetString(61u, 3u, "[]");
		player.Attributes.SyncTo(sync, attr7);
		ClearRelics(player, sync);
		return jsonArray;
	}

	public static void AccumulateRewards(PlayerInstance player, IReadOnlyList<IReadOnlyList<uint>> rows, NtfSyncPlayer sync)
	{
		if (rows.Count == 0)
		{
			return;
		}
		JsonArray jsonArray = (JsonNode.Parse(player.Attributes.GetStringValue(61u, 3u) ?? "[]") as JsonArray) ?? new JsonArray();
		foreach (IReadOnlyList<uint> row in rows)
		{
			JsonArray jsonArray2 = new JsonArray();
			foreach (uint item in row)
			{
				jsonArray2.Add(item);
			}
			jsonArray.Add(jsonArray2);
		}
		PlayerStrAttr attr = player.Attributes.SetString(61u, 3u, jsonArray.ToJsonString());
		player.Attributes.SyncTo(sync, attr);
	}

	public static void RecordEnding(PlayerInstance player, uint endingType, uint score, uint time, NtfSyncPlayer sync)
	{
		if (endingType != 0 && endingType < 32)
		{
			uint num = player.Attributes.Get(142u, 15u)?.Val ?? 0;
			PlayerAttr attr = player.Attributes.Set(142u, 15u, num | (uint)(1 << (int)endingType));
			player.Attributes.SyncTo(sync, attr);
		}
		JsonObject jsonObject = new JsonObject
		{
			["roleformation"] = new JsonArray(),
			["TotalBestScore"] = score,
			["TotalBestTime"] = time,
			["BestScore"] = score,
			["BestTime"] = time,
			["CurScore"] = score,
			["CurTime"] = time
		};
		PlayerStrAttr attr2 = player.Attributes.SetString(61u, 5u, jsonObject.ToJsonString());
		player.Attributes.SyncTo(sync, attr2);
	}

	public static BagRogueNodeExcel? CurrentNode(PlayerInstance player)
	{
		uint value = player.Attributes.GetValue(142u, 6u);
		BagRogueNodeExcel value2;
		return (value != 0 && GameData.BagRogueNodeData.TryGetValue(value, out value2)) ? value2 : null;
	}

	public static List<List<uint>> NodeRewardRows(BagRogueNodeExcel node)
	{
		return node.Rewards;
	}

	public static uint ReadUint(JsonNode? node, uint fallback = 0u)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : fallback;
	}
}
