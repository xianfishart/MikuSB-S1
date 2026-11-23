using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

public static class TaskProgressService
{
	public const uint TaskGid = 2u;

	private static List<JsonObject>? _tasks;

	private static readonly HashSet<int> DerivableTypes = new HashSet<int>
	{
		1, 3, 10, 14, 21, 26, 30, 31, 38, 42,
		46, 49, 50, 72, 75, 91, 93, 94, 103, 350,
		47, 23
	};

	private static HashSet<int>? _forcedComplete;

	public static void Ensure(PlayerInstance player)
	{
		// 第 415 节：日常（Group=1）先按天清零，再算主线/目标（Group=3/4）的派生进度。
		TaskEventService.EnsureDailyReset(player);
		TaskEventService.EnsureWeeklyReset(player);
		foreach (JsonObject item in LoadTasks())
		{
			int num = Num(item, "Group");
			if ((uint)(num - 3) > 1u)
			{
				continue;
			}
			int num2 = Num(item, "ID");
			int num3 = Num(item, "CheckValue");
			if (num2 > 0 && num3 > 0 && (IsDerivable(item) || IsForced(num2)))
			{
				int num4 = Evaluate(player, item, num3);
				PlayerAttr attr = GetAttr(player, 2u, (uint)num2, create: true);
				if (num4 <= 0)
				{
					attr.Val &= 1u;
					continue;
				}
				num4 = Math.Min(num4, num3);
				attr.Val = (attr.Val & 1) | (uint)(num4 << 1);
			}
		}
		// 第 415 节：登录时按已完成的日常任务重算执行度（8/1），
		// 否则这个属性可能不存在/为 null，客户端「执行度」数字会显示成方框。
		DailyPointService.Recompute(player);
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	public static async Task<List<JsonArray>> Claim(Connection connection, int taskId, NtfSyncPlayer sync)
	{
		List<JsonArray> result = new List<JsonArray>();
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return result;
		}
		JsonObject row = LoadTasks().FirstOrDefault((JsonObject x) => Num(x, "ID") == taskId);
		if (row == null)
		{
			return result;
		}
		int group = Num(row, "Group");
		PlayerAttr attr = GetAttr(player, 2u, (uint)taskId, create: true);
		if (group == 5)
		{
			int chapter = Num(row, "ChapterGroup");
			IEnumerable<JsonObject> branch = from x in LoadTasks()
				where Num(x, "Group") == 4 && Num(x, "ChapterGroup") == chapter
				select x;
			foreach (JsonObject task in branch)
			{
				PlayerAttr done = GetAttr(player, 2u, (uint)Num(task, "ID"), create: false);
				if (done == null || (done.Val & 1) == 0)
				{
					return result;
				}
			}
			if ((attr.Val & 1) != 0)
			{
				return result;
			}
		}
		else
		{
			int need = Num(row, "CheckValue");
			if (need <= 0 || attr.Val >> 1 < (uint)need || (attr.Val & 1) != 0)
			{
				return result;
			}
		}
		JsonNode jsonNode = row["Rewards"];
		if (jsonNode is JsonArray rewards)
		{
			foreach (JsonArray node in rewards.OfType<JsonArray>())
			{
				List<uint> values = node.Select(ReadUInt).Take(5).ToList();
				if (values.Count >= 5 && values[0] != 0)
				{
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, values);
				}
			}
			if (rewards.Count > 0)
			{
				result.Add(rewards);
			}
		}
		attr.Val |= 1u;
		sync.Custom[player.ToPackedAttrKey(2u, (uint)taskId)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(2u, (uint)taskId)] = attr.Val;
		TaskEventService.AddByType(player, 42, 1);
		return result;
	}

	public static void Save(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
	}

	private static bool IsForced(int id)
	{
		if (_forcedComplete == null)
		{
			_forcedComplete = new HashSet<int>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "forced_complete.json");
				if (File.Exists(path))
				{
					JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
					if (jsonArray != null)
					{
						foreach (JsonNode item in jsonArray)
						{
							if (int.TryParse(item?.ToString(), out var result) && result > 0)
							{
								_forcedComplete.Add(result);
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _forcedComplete.Contains(id);
	}

	private static bool IsDerivable(JsonObject row)
	{
		return (DerivableTypes.Contains(Num(row, "Type")) && !IsForced(Num(row, "ID"))) || IsForced(Num(row, "ID"));
	}

	private static int Evaluate(PlayerInstance player, JsonObject row, int check)
	{
		if (IsForced(Num(row, "ID")))
		{
			return check;
		}
		int num = Num(row, "Type");
		if (1 == 0)
		{
		}
		int result;
		switch (num)
		{
		case 23:
			result = (player.InventoryManager.InventoryData.Weapons.Values.Any((GameWeaponInfo w) => w.Break >= 2) ? check : 0);
			break;
		case 47:
			result = (player.CharacterManager.CharacterData.Characters.Any((CharacterInfo c) => c.Break != 0) ? check : 0);
			break;
		case 1:
			result = (int)player.Data.Level;
			break;
		case 3:
			result = CountType3(player, row);
			break;
		case 14:
			result = Enumerable.Count(player.Data.Attrs, (PlayerAttr x) => x.Gid == 22 && x.Val != 0);
			break;
		case 21:
			result = Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => x.Level > 1);
			break;
		case 26:
			result = player.CharacterManager.CharacterData.Characters.Count;
			break;
		case 30:
			result = CountCharacters(player, row);
			break;
		case 31:
			result = CountWeapons(player, row);
			break;
		case 38:
			result = Enumerable.Count(player.InventoryManager.InventoryData.Weapons.Values, (GameWeaponInfo x) => x.Level > 1);
			break;
		case 42:
			result = Enumerable.Count(player.Data.Attrs, (PlayerAttr x) => x.Gid == 2 && (x.Val & 1) != 0);
			break;
		case 46:
			result = CountSupport(player, row);
			break;
		case 49:
			result = CountOrangeItems(player, row);
			break;
		case 10:
		case 91:
			result = (AnyLevelPassed(player, row) ? check : 0);
			break;
		case 93:
			result = check;
			break;
		case 50:
			result = ((TotalSpines(player) >= check) ? check : 0);
			break;
		case 103:
			result = TotalSpines(player);
			break;
		case 72:
			result = CountSpineStage(player, row);
			break;
		case 75:
			result = ((player.InventoryManager.InventoryData.Weapons.Values.Sum((GameWeaponInfo x) => x.PartSlots.Count) >= check) ? check : 0);
			break;
		case 94:
			result = (AnySpineComplete(player, row) ? check : 0);
			break;
		case 350:
			result = (HasPassedChapter(player, row) ? check : 0);
			break;
		default:
			result = 0;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool AnyLevelPassed(PlayerInstance player, JsonObject row)
	{
		if (!(row["Param"] is JsonArray source))
		{
			return false;
		}
		HashSet<uint> passed = (from x in player.Data.Attrs
			where x.Gid == 22 && x.Val != 0
			select x.Sid).ToHashSet();
		return source.Any((JsonNode x) => uint.TryParse(x?.ToString(), out var result) && passed.Contains(result));
	}

	private static int TotalSpines(PlayerInstance player)
	{
		return player.CharacterManager.CharacterData.Characters.Sum((CharacterInfo x) => x.Spines.Count);
	}

	private static int CountSpineStage(PlayerInstance player, JsonObject row)
	{
		int stage = ((!(row["Param"] is JsonArray { Count: >0 } jsonArray)) ? 1 : NumValue(jsonArray[0]));
		if (stage <= 0)
		{
			stage = 1;
		}
		return Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => x.Spines.Count >= stage);
	}

	private static bool AnySpineComplete(PlayerInstance player, JsonObject row)
	{
		int need = ((row["Param"] is JsonArray { Count: >0 } jsonArray) ? NumValue(jsonArray[0]) : 3);
		if (need <= 0)
		{
			need = 3;
		}
		return player.CharacterManager.CharacterData.Characters.Any((CharacterInfo x) => x.Spines.Count >= need);
	}

	private static int CountSupport(PlayerInstance player, JsonObject row)
	{
		int need = ((row["Param"] is JsonArray { Count: >0 } jsonArray) ? NumValue(jsonArray[0]) : 0);
		return Enumerable.Count(player.InventoryManager.InventoryData.SupportCards.Values, (GameSupportCardInfo x) => x.Level >= need);
	}

	private static int CountOrangeItems(PlayerInstance player, JsonObject row)
	{
		if (row["Param"] is JsonArray { Count: >=2 } jsonArray)
		{
			int num = NumValue(jsonArray[0]);
			int color = NumValue(jsonArray[1]);
			if (color <= 0)
			{
				color = 5;
			}
			switch (num)
			{
			case 1:
			{
				HashSet<ulong> owned = player.CharacterManager.CharacterData.Characters.Select((CharacterInfo x) => x.TemplateId).ToHashSet();
				return Enumerable.Count(GameData.CardData.Values, (CardExcel c) => c.Color >= color && owned.Contains(GameResourceTemplateId.FromGdpl(c.Genre, c.Detail, c.Particular, c.Level)));
			}
			case 2:
			{
				HashSet<ulong> templates = (from x in GameData.WeaponData.Values
					where x.Color >= color
					select GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level)).ToHashSet();
				return Enumerable.Count(player.InventoryManager.InventoryData.Weapons.Values, (GameWeaponInfo w) => templates.Contains(w.TemplateId));
			}
			default:
				return 0;
			}
		}
		return 0;
	}

	private static int CountCharacters(PlayerInstance player, JsonObject row)
	{
		int need = ((row["Param"] is JsonArray { Count: >0 } jsonArray) ? NumValue(jsonArray[0]) : 0);
		return (need > 0) ? Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => x.Level >= need) : 0;
	}

	public static int CountType3(PlayerInstance player, JsonObject row)
	{
		int num = ((row["Param"] is JsonArray { Count: >0 } jsonArray) ? NumValue(jsonArray[0]) : 0);
		if (num == 3)
		{
			return player.InventoryManager.InventoryData.SupportCards.Count;
		}
		return player.InventoryManager.InventoryData.Items.Count;
	}

	private static int CountWeapons(PlayerInstance player, JsonObject row)
	{
		int need = ((row["Param"] is JsonArray { Count: >0 } jsonArray) ? NumValue(jsonArray[0]) : 0);
		return (need > 0) ? Enumerable.Count(player.InventoryManager.InventoryData.Weapons.Values, (GameWeaponInfo x) => x.Level >= need) : 0;
	}

	private static bool HasPassedChapter(PlayerInstance player, JsonObject row)
	{
		if (!(row["Param"] is JsonArray { Count: >=2 } jsonArray) || !(jsonArray[1] is JsonArray jsonArray2))
		{
			return false;
		}
		foreach (JsonNode item in jsonArray2)
		{
			int num = NumValue(item);
			if (num > 0 && (player.GetAttr(22u, (uint)num) != 0 || player.GetAttr(21u, (uint)num) != 0))
			{
				return true;
			}
		}
		return false;
	}

	public static List<JsonObject> LoadTasks()
	{
		if (_tasks != null)
		{
			return _tasks;
		}
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "achievement.json");
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
			_tasks = (List<JsonObject>?)obj;
		}
		catch
		{
			_tasks = new List<JsonObject>();
		}
		return _tasks;
	}

	private static PlayerAttr? GetAttr(PlayerInstance player, uint gid, uint sid, bool create)
	{
		// 第 415 节：走 Attributes 缓存，别直接摸 player.Data.Attrs，
		// 否则新建的条目不会进 (Gid,Sid)->PlayerAttr 字典，GetProgress 一直是 0。
		if (create)
		{
			return player.Attributes.GetOrCreate(gid, sid);
		}
		return player.Attributes.Get(gid, sid);
	}

	public static int Num(JsonObject row, string key)
	{
		return NumValue(row[key]);
	}

	public static int NumValue(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
