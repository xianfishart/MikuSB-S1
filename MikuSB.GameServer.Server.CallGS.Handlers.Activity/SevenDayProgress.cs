using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

public static class SevenDayProgress
{
	public const int ActivityId = 5;

	public const uint ActivityGroup = 50u;

	public const uint DaySid = 53u;

	public const uint AwardBitsSid = 54u;

	public const uint TokenGid = 1u;

	public const uint TokenSid = 11u;

	public static void Ensure(PlayerInstance player)
	{
		try
		{
			SetAttr(player, 50u, 53u, 7u);
			EvaluateTasks(player);
			PlayerAttr attr = GetAttr(player, 1u, 11u, create: true);
			attr.Val = (uint)Math.Clamp(CalculatePoints(player), 0, 100);
			EnsurePlayerLevelTasks(player);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		catch
		{
		}
	}

	private static void EvaluateTasks(PlayerInstance player)
	{
		foreach (JsonObject item in LoadQuestRows())
		{
			int num = Num(item, "ID");
			if (num < 101 || num > 707)
			{
				continue;
			}
			int num2 = Num(item, "Type");
			int num3 = Math.Max(1, Num(item, "CheckValue"));
			JsonArray param = ((item["Param"] is JsonArray jsonArray) ? jsonArray : null);
			int num4 = num2;
			if (1 == 0)
			{
			}
			int num5;
			if (num4 <= 31)
			{
				if (num4 != 30)
				{
					if (num4 != 31)
					{
						goto IL_0105;
					}
					num5 = CountWeaponsAtLevel(player, param, num3);
				}
				else
				{
					num5 = CountCharactersAtLevel(player, param, num3);
				}
			}
			else if (num4 != 52)
			{
				if (num4 != 350)
				{
					goto IL_0105;
				}
				num5 = (HasPassedChapter(player, param) ? num3 : 0);
			}
			else
			{
				if ((num != 202 && num != 606) || 1 == 0)
				{
					goto IL_0105;
				}
				num5 = num3;
			}
			goto IL_010a;
			IL_010a:
			if (1 == 0)
			{
			}
			int value = num5;
			value = Math.Clamp(value, 0, num3);
			PlayerAttr attr = GetAttr(player, 7u, (uint)num, create: true);
			uint num6 = attr.Val & 1;
			attr.Val = num6 | (uint)(value << 1);
			continue;
			IL_0105:
			num5 = 0;
			goto IL_010a;
		}
	}

	private static int CalculatePoints(PlayerInstance player)
	{
		int num = 0;
		foreach (JsonObject item in LoadQuestRows())
		{
			int num2 = Num(item, "ID");
			if (num2 < 101 || num2 > 707)
			{
				continue;
			}
			PlayerAttr attr = GetAttr(player, 7u, (uint)num2, create: false);
			if (attr != null)
			{
				int num3 = Math.Max(1, Num(item, "CheckValue"));
				if (attr.Val >> 1 >= (uint)num3)
				{
					int num4 = (int)((item["Rewards"] is JsonArray { Count: >0 } jsonArray && jsonArray[0] is JsonArray { Count: >=5 } jsonArray2) ? ReadUInt(jsonArray2[4]) : 20);
					num += num4;
				}
			}
		}
		return num;
	}

	private static int CountCharactersAtLevel(PlayerInstance player, JsonArray? param, int check)
	{
		int? need = ((param == null || param.Count <= 0) ? ((int?)null) : param[0]?.GetValue<int>());
		if (!need.HasValue)
		{
			return 0;
		}
		return Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => x.Level >= need.Value);
	}

	private static int CountWeaponsAtLevel(PlayerInstance player, JsonArray? param, int check)
	{
		int? need = ((param == null || param.Count <= 0) ? ((int?)null) : param[0]?.GetValue<int>());
		if (!need.HasValue)
		{
			return 0;
		}
		return Enumerable.Count(player.InventoryManager.InventoryData.Items.Values, (BaseGameItemInfo x) => x.ItemType == ItemTypeEnum.TYPE_WEAPON && x.Level >= need.Value);
	}

	private static bool HasPassedChapter(PlayerInstance player, JsonArray? param)
	{
		if (param == null || param.Count < 2 || !(param[1] is JsonArray jsonArray))
		{
			return false;
		}
		foreach (JsonNode item in jsonArray)
		{
			if (!(item is JsonValue jsonValue) || !jsonValue.TryGetValue<int>(out var value) || value <= 0 || (player.GetAttr(22u, (uint)value) == 0 && player.GetAttr(21u, (uint)value) == 0))
			{
				continue;
			}
			return true;
		}
		return false;
	}

	public static void EnsurePlayerLevelTasks(PlayerInstance player)
	{
		foreach (JsonObject item in LoadQuestRows())
		{
			int num = Num(item, "ID");
			if (num >= 9101 && num <= 9116 && Num(item, "Type") == 187)
			{
				int num2 = Num(item, "CheckValue");
				if (num2 > 0)
				{
					bool flag = player.Data.Level >= num2;
					PlayerAttr attr = GetAttr(player, 7u, (uint)num, create: true);
					uint num3 = attr.Val & 1;
					long num4 = (flag ? num2 : Math.Min(player.Data.Level, num2 - 1));
					attr.Val = num3 | (uint)((int)num4 << 1);
				}
			}
		}
	}

	public static async Task ClaimAchievement(Connection connection, int achievementId)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject row = LoadQuestRows().FirstOrDefault((JsonObject x) => Num(x, "ID") == achievementId);
		if (row == null)
		{
			await CallGSRouter.SendScript(connection, "GetAchievementAwardRsp_SevenDay", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerAttr attr = GetAttr(player, 7u, (uint)achievementId, create: false);
		if (attr == null || (attr.Val & 2) == 0)
		{
			await CallGSRouter.SendScript(connection, "GetAchievementAwardRsp_SevenDay", "{\"sErr\":\"achievement.NotFinished\"}");
			return;
		}
		if ((attr.Val & 1) != 0)
		{
			await CallGSRouter.SendScript(connection, "GetAchievementAwardRsp_SevenDay", "{\"sErr\":\"achievement.RewardGeted\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonNode jsonNode = row["Rewards"];
		if (jsonNode is JsonArray rewards)
		{
			await GrantRewards(player, sync, rewards);
		}
		attr.Val |= 1u;
		MarkSeen(player, sync);
		sync.Custom[player.ToPackedAttrKey(7u, (uint)achievementId)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(7u, (uint)achievementId)] = attr.Val;
		Save(player);
		JsonObject response = new JsonObject
		{
			["achievementId"] = achievementId,
			["actId"] = 5
		};
		await CallGSRouter.SendScript(connection, "GetAchievementAwardRsp_SevenDay", response.ToJsonString(), sync);
	}

	public static async Task ClaimIndex(Connection connection, int actId, int index)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JsonObject award = LoadAwardRows().FirstOrDefault((JsonObject x) => Num(x, "ActId") == actId && Num(x, "AwardId") == index);
		if (award == null)
		{
			await CallGSRouter.SendScript(connection, "GetIndexAwardRsp_SevenDay", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerAttr bits = GetAttr(player, 50u, 54u, create: true);
		if ((bits.Val & (uint)(1 << index)) != 0)
		{
			await CallGSRouter.SendScript(connection, "GetIndexAwardRsp_SevenDay", "{\"sErr\":\"achievement.RewardGeted\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonNode jsonNode = award["ItemAward"];
		if (jsonNode is JsonArray itemAward)
		{
			await GrantReward(player, sync, itemAward);
		}
		bits.Val |= (uint)(1 << index);
		MarkSeen(player, sync);
		sync.Custom[player.ToPackedAttrKey(50u, 54u)] = bits.Val;
		sync.Custom[player.ToShiftedAttrKey(50u, 54u)] = bits.Val;
		Save(player);
		JsonObject response = new JsonObject
		{
			["actId"] = actId,
			["index"] = index
		};
		await CallGSRouter.SendScript(connection, "GetIndexAwardRsp_SevenDay", response.ToJsonString(), sync);
	}

	public static async Task ClaimAll(Connection connection, JsonObject request)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int actId = Num(request, "actId");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray allRewards = new JsonArray();
		JsonNode jsonNode = request["tbAchieveInfo"];
		if (jsonNode is JsonArray achievements)
		{
			foreach (JsonObject node in achievements.OfType<JsonObject>())
			{
				int achievementId = Num(node, "achievementId");
				if (achievementId <= 0)
				{
					continue;
				}
				JsonObject row = LoadQuestRows().FirstOrDefault((JsonObject x) => Num(x, "ID") == achievementId);
				if (row == null)
				{
					continue;
				}
				PlayerAttr attr = GetAttr(player, 7u, (uint)achievementId, create: false);
				if (attr != null && (attr.Val & 2) != 0 && (attr.Val & 1) == 0)
				{
					jsonNode = row["Rewards"];
					if (jsonNode is JsonArray rewards)
					{
						allRewards.Add(rewards.DeepClone());
						await GrantRewards(player, sync, rewards);
					}
					attr.Val |= 1u;
					sync.Custom[player.ToPackedAttrKey(7u, (uint)achievementId)] = attr.Val;
					sync.Custom[player.ToShiftedAttrKey(7u, (uint)achievementId)] = attr.Val;
				}
			}
		}
		PlayerAttr bits = GetAttr(player, 50u, 54u, create: true);
		PlayerAttr token = GetAttr(player, 1u, 11u, create: true);
		jsonNode = request["tbIndexId"];
		HashSet<int> requestedIndexes = ((jsonNode is JsonArray ids) ? (from x in ids
			select int.TryParse(x?.ToString(), out var result) ? result : 0 into x
			where x > 0
			select x).ToHashSet() : new HashSet<int>());
		foreach (JsonObject award in from x in LoadAwardRows()
			where Num(x, "ActId") == actId
			select x)
		{
			int index = Num(award, "AwardId");
			if (index <= 0 || (bits.Val & (uint)(1 << index)) != 0 || (requestedIndexes.Count > 0 && !requestedIndexes.Contains(index)))
			{
				continue;
			}
			int needPoint = Num(award, "NeedPoint");
			if (needPoint <= token.Val)
			{
				jsonNode = award["ItemAward"];
				if (jsonNode is JsonArray itemAward)
				{
					allRewards.Add(new JsonArray { itemAward.DeepClone() });
					await GrantReward(player, sync, itemAward);
				}
				bits.Val |= (uint)(1 << index);
			}
		}
		MarkSeen(player, sync);
		sync.Custom[player.ToPackedAttrKey(50u, 54u)] = bits.Val;
		sync.Custom[player.ToShiftedAttrKey(50u, 54u)] = bits.Val;
		Save(player);
		await CallGSRouter.SendScript(connection, "GetAllAwardRsp_SevenDay", allRewards.ToJsonString(), sync);
	}

	private static async Task GrantRewards(PlayerInstance player, NtfSyncPlayer sync, JsonArray rewards)
	{
		foreach (JsonNode node in rewards)
		{
			if (node is JsonArray row)
			{
				List<uint> reward = row.Select(ReadUInt).Take(5).ToList();
				if (reward.Count >= 5 && reward[0] != 0)
				{
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
				}
			}
		}
	}

	private static async Task GrantReward(PlayerInstance player, NtfSyncPlayer sync, JsonArray reward)
	{
		List<uint> values = reward.Select(ReadUInt).Take(5).ToList();
		if (values.Count >= 5 && values[0] != 0)
		{
			await IBLogic_BuyGoods.GrantRewardAsync(player, sync, values);
		}
	}

	private static int[] LoadAchievementIds()
	{
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "activity";
			buffer[3] = "seven_day";
			buffer[4] = "seven_day_achievements.json";
			string path = Path.Combine(buffer);
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			return ((jsonArray != null) ? (from x in jsonArray.OfType<JsonObject>()
				where Num(x, "ActId") == 5
				select x).SelectMany((JsonObject x) =>
			{
				JsonArray obj2 = x["AchieveId"] as JsonArray;
				return ((obj2 != null) ? obj2.Select((JsonNode v) => v.GetValue<int>()) : null) ?? Array.Empty<int>();
			}).ToArray() : null) ?? Array.Empty<int>();
		}
		catch
		{
			return Array.Empty<int>();
		}
	}

	private static List<JsonObject> LoadQuestRows()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			JsonNode? jsonNode = JsonNode.Parse(File.ReadAllText(path));
			object obj;
			if (jsonNode == null)
			{
				obj = null;
			}
			else
			{
				JsonArray jsonArray = jsonNode.AsArray();
				obj = ((jsonArray != null) ? jsonArray.OfType<JsonObject>().ToList() : null);
			}
			if (obj == null)
			{
				obj = new List<JsonObject>();
			}
			return (List<JsonObject>)obj;
		}
		catch
		{
			return new List<JsonObject>();
		}
	}

	private static List<JsonObject> LoadAwardRows()
	{
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "activity";
			buffer[3] = "seven_day";
			buffer[4] = "seven_day_awards.json";
			string path = Path.Combine(buffer);
			JsonNode? jsonNode = JsonNode.Parse(File.ReadAllText(path));
			object obj;
			if (jsonNode == null)
			{
				obj = null;
			}
			else
			{
				JsonArray jsonArray = jsonNode.AsArray();
				obj = ((jsonArray != null) ? jsonArray.OfType<JsonObject>().ToList() : null);
			}
			if (obj == null)
			{
				obj = new List<JsonObject>();
			}
			return (List<JsonObject>)obj;
		}
		catch
		{
			return new List<JsonObject>();
		}
	}

	private static PlayerAttr? GetAttr(PlayerInstance player, uint gid, uint sid, bool create)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if ((playerAttr == null) & create)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		return playerAttr;
	}

	private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint val)
	{
		PlayerAttr attr = GetAttr(player, gid, sid, create: true);
		attr.Val = val;
	}

	private static void MarkSeen(PlayerInstance player, NtfSyncPlayer sync)
	{
		PlayerAttr attr = GetAttr(player, 50u, 51u, create: true);
		if ((attr.Val & 2) == 0)
		{
			attr.Val |= 2u;
		}
		sync.Custom[player.ToPackedAttrKey(50u, 51u)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(50u, 51u)] = attr.Val;
	}

	private static void Save(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
	}

	public static int Num(JsonObject row, string key)
	{
		int result;
		return int.TryParse(row[key]?.ToString(), out result) ? result : 0;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
