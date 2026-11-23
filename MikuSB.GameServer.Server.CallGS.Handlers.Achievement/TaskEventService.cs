using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

public static class TaskEventService
{
	private const uint TaskGid = 2u;

	private const uint ResetMarkerGid = 999u;

	private const uint ResetMarkerSid = 5u;

	/// <summary>
	/// 第 415 节：日常任务（Group=1）的每日重置标记，存在 999/4。
	/// 999/0 是宿舍每日闲聊、999/1 是宿舍许愿、999/2 是宿舍掉落、999/3 是断片互赠，
	/// 999/5 是定期（周）任务，都已被占用，所以日常用 4。
	/// </summary>
	private const uint DailyResetSid = 4u;

	/// <summary>日常任务组号（客户端「任务 - 日常」页）。</summary>
	private const int DailyGroup = 1;

	/// <summary>执行度属性：8/1 = 当前分数，8/10 起是各档里程碑的领取标记。</summary>
	private const uint PointGid = 8u;

	private const uint PointSid = 1u;

	private const uint PointRewardStartSid = 10u;

	public static void EnsureWeeklyReset(PlayerInstance player)
	{
		DateTime dateTime = DateTime.Now.AddHours(-4.0);
		uint num = uint.Parse(dateTime.Date.AddDays(-((int)(dateTime.DayOfWeek + 6) % 7)).ToString("yyyyMMdd"));
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 999 && x.Sid == 5);
		if (playerAttr != null && playerAttr.Val == num)
		{
			return;
		}
		foreach (JsonObject item in from x in TaskProgressService.LoadTasks()
			where TaskProgressService.Num(x, "Group") == 2
			select x)
		{
			int id = TaskProgressService.Num(item, "ID");
			PlayerAttr playerAttr2 = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == (uint)id);
			if (playerAttr2 != null)
			{
				playerAttr2.Val = 0u;
			}
		}
		if (playerAttr == null)
		{
			playerAttr = player.Attributes.GetOrCreate(999u, 5u);
		}
		playerAttr.Val = num;
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	/// <summary>
	/// 第 415 节：日常任务每日重置（凌晨 4 点换日，与周常口径一致）。
	/// 之前只做了周常重置，日常任务的进度属性既没人写、也没人清，客户端「日常」页永远是 0/N。
	/// 这里把 Group=1 的任务进度、执行度分数、执行度里程碑领取标记一起归零。
	/// </summary>
	public static void EnsureDailyReset(PlayerInstance player)
	{
		uint num = uint.Parse(DateTime.Now.AddHours(-4.0).Date.ToString("yyyyMMdd"));
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == ResetMarkerGid && x.Sid == DailyResetSid);
		if (playerAttr != null && playerAttr.Val == num)
		{
			return;
		}
		foreach (JsonObject item in TaskProgressService.LoadTasks())
		{
			if (TaskProgressService.Num(item, "Group") != DailyGroup)
			{
				continue;
			}
			int id = TaskProgressService.Num(item, "ID");
			PlayerAttr playerAttr2 = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == (uint)id);
			if (playerAttr2 != null)
			{
				playerAttr2.Val = 0u;
			}
		}
		foreach (PlayerAttr item2 in player.Data.Attrs)
		{
			if (item2.Gid == PointGid && item2.Sid >= PointSid)
			{
				item2.Val = 0u;
			}
		}
		if (playerAttr == null)
		{
			playerAttr = player.Attributes.GetOrCreate(ResetMarkerGid, DailyResetSid);
		}
		playerAttr.Val = num;
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	public static void OnLevelSettlement(PlayerInstance player, JsonObject? levelNode, JsonNode? tbParam)
	{
		if (levelNode == null || tbParam == null)
		{
			return;
		}
		if (levelNode["ConsumeVigor"] is JsonArray { Count: >0 } jsonArray)
		{
			int num = TaskProgressService.NumValue(jsonArray[0]);
			if (num > 0)
			{
				AddByType(player, 4, num);
			}
		}
		int num2 = TaskProgressService.NumValue(tbParam["nDestructibleNum"]);
		if (num2 > 0)
		{
			AddByType(player, 116, num2);
		}
		if (tbParam["tbKill"] is JsonArray jsonArray2)
		{
			int num3 = jsonArray2.Sum((JsonNode x) => TaskProgressService.NumValue(x));
			if (num3 > 0)
			{
				AddByType(player, 77, num3);
				AddByType(player, 18, jsonArray2.Count);
			}
		}
		Save(player);
	}

	public static void OnLevelClear(PlayerInstance player, uint levelId)
	{
		OnLevelClearCore(player, levelId);
		Save(player);
	}

	/// <summary>
	/// 第 415 节：速战（Daily_LevelMoppingUp）可能一次打多把，
	/// 逐把记通关次数，全部记完只落库/推送一次。
	/// </summary>
	public static void OnLevelClearMany(PlayerInstance player, uint levelId, int times)
	{
		if (times <= 0)
		{
			return;
		}
		for (int i = 0; i < times; i++)
		{
			OnLevelClearCore(player, levelId);
		}
		Save(player);
	}

	private static void OnLevelClearCore(PlayerInstance player, uint levelId)
	{
		foreach (JsonObject item in TaskProgressService.LoadTasks())
		{
			int num = TaskProgressService.Num(item, "Group");
			if (!TracksProgress(num))
			{
				continue;
			}
			int num2 = TaskProgressService.Num(item, "Type");
			int num3 = TaskProgressService.Num(item, "ID");
			int num4 = TaskProgressService.Num(item, "CheckValue");
			if (num3 > 0 && num4 > 0)
			{
				if (1 == 0)
				{
				}
				bool flag;
				switch (num2)
				{
				case 14:
					flag = true;
					break;
				case 10:
				case 91:
					flag = ParamHasLevel(item, levelId);
					break;
				case 350:
					flag = ContainsLevel(item, levelId);
					break;
				case 60:
					// 「完成常规行动 N 次」：只有日常关卡（资源本）才算。
					flag = GameData.DailyLevelData.ContainsKey(levelId);
					break;
				case 68:
					// 「通关任意队员个人故事 N 次」：个人故事关卡在 challenge/role/level.json。
					flag = GameData.RoleLevelData.ContainsKey(levelId);
					break;
				default:
					flag = false;
					break;
				}
				if (1 == 0)
				{
				}
				if (flag)
				{
					Add(player, num3, num4, 1);
				}
			}
		}
	}

	public static void OnCharacterLevelUp(PlayerInstance player, int levels)
	{
		if (levels <= 0)
		{
			return;
		}
		foreach (JsonObject item in TaskProgressService.LoadTasks())
		{
			int num = TaskProgressService.Num(item, "Group");
			if (TracksProgress(num) && TaskProgressService.Num(item, "Type") == 21)
			{
				int num2 = TaskProgressService.Num(item, "ID");
				int num3 = TaskProgressService.Num(item, "CheckValue");
				if (num2 > 0 && num3 > 0)
				{
					Add(player, num2, num3, levels);
				}
			}
		}
		Save(player);
	}

	public static void SyncType(PlayerInstance player, int type, NtfSyncPlayer sync)
	{
		foreach (JsonObject item in TaskProgressService.LoadTasks())
		{
			int num = TaskProgressService.Num(item, "Group");
			bool flag = TracksProgress(num);
			if (flag && TaskProgressService.Num(item, "Type") == type)
			{
				int id = TaskProgressService.Num(item, "ID");
				PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == (uint)id);
				if (playerAttr != null)
				{
					sync.Custom[player.ToPackedAttrKey(2u, (uint)id)] = playerAttr.Val;
					sync.Custom[player.ToShiftedAttrKey(2u, (uint)id)] = playerAttr.Val;
				}
			}
		}
	}

	public static void AddByType(PlayerInstance player, int type, int amount)
	{
		if (amount <= 0)
		{
			return;
		}
		foreach (JsonObject item in TaskProgressService.LoadTasks())
		{
			int num = TaskProgressService.Num(item, "Group");
			bool flag = TracksProgress(num);
			if (flag && TaskProgressService.Num(item, "Type") == type)
			{
				int num2 = TaskProgressService.Num(item, "ID");
				int num3 = TaskProgressService.Num(item, "CheckValue");
				if (num2 > 0 && num3 > 0)
				{
					Add(player, num2, num3, amount);
				}
			}
		}
	}

	public static void RefreshSnapshot(PlayerInstance player, params int[] types)
	{
		HashSet<int> hashSet = types.ToHashSet();
		foreach (JsonObject item in TaskProgressService.LoadTasks())
		{
			int num = TaskProgressService.Num(item, "Group");
			if (!TracksProgress(num))
			{
				continue;
			}
			int num2 = TaskProgressService.Num(item, "Type");
			if (!hashSet.Contains(num2))
			{
				continue;
			}
			int id = TaskProgressService.Num(item, "ID");
			int num3 = TaskProgressService.Num(item, "CheckValue");
			PlayerAttr playerAttr = player.Attributes.GetOrCreate(2u, (uint)id);
			if (id > 0 && num3 > 0)
			{
				if (1 == 0)
				{
				}
				int num4 = num2 switch
				{
					1 => (int)player.Data.Level, 
					3 => TaskProgressService.CountType3(player, item), 
					26 => player.CharacterManager.CharacterData.Characters.Count, 
					30 => CountCharacters(player, item), 
					31 => CountWeapons(player, item), 
					46 => CountSupport(player, item), 
					49 => CountOrangeCharacters(player), 
					50 => player.CharacterManager.CharacterData.Characters.Sum((CharacterInfo x) => x.Spines.Count), 
					72 => Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => x.Spines.Count >= 2), 
					75 => player.InventoryManager.InventoryData.Weapons.Values.Sum((GameWeaponInfo x) => x.PartSlots.Count), 
					103 => player.CharacterManager.CharacterData.Characters.Sum((CharacterInfo x) => x.Spines.Count), 
					117 => TotalClips(player), 
					121 => CountFavor20(player), 
					122 => CountFurniture(player), 
					123 => CountRooms(player), 
					124 => (int)player.GetAttr(101u, 100u), 
					350 => HasPassedChapter(player, item) ? num3 : 0, 
					_ => 0, 
				};
				if (1 == 0)
				{
				}
				int num5 = num4;
				if (num5 <= 0)
				{
					playerAttr.Val &= 1u;
				}
				else
				{
					playerAttr.Val = (playerAttr.Val & 1) | (uint)(Math.Min(num5, num3) << 1);
				}
			}
		}
		Save(player);
	}

	public static void OnWeaponUpgrade(PlayerInstance player, int count = 1)
	{
		AddByType(player, 38, count);
		RefreshSnapshot(player, 31, 3);
		Save(player);
	}

	public static void OnWeaponBreak(PlayerInstance player)
	{
		AddByType(player, 23, 1);
		Save(player);
	}

	public static void OnShopBuy(PlayerInstance player, int count = 1)
	{
		AddByType(player, 8, count);
		Save(player);
	}

	public static void OnSupportUpgrade(PlayerInstance player)
	{
		AddByType(player, 44, 1);
		RefreshSnapshot(player, 46);
	}

	public static void OnSpineNode(PlayerInstance player)
	{
		AddByType(player, 47, 1);
		AddByType(player, 50, 1);
		AddByType(player, 103, 1);
		Save(player);
	}

	public static void OnFavorChanged(PlayerInstance player)
	{
		RefreshSnapshot(player, 121);
	}

	public static void OnClipCollected(PlayerInstance player)
	{
		AddByType(player, 117, 1);
		Save(player);
	}

	private static int CountCharacters(PlayerInstance player, JsonObject task)
	{
		int need = ((task["Param"] is JsonArray { Count: >0 } jsonArray) ? TaskProgressService.NumValue(jsonArray[0]) : 0);
		return (need <= 0) ? player.CharacterManager.CharacterData.Characters.Count : Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => x.Level >= need);
	}

	private static int CountWeapons(PlayerInstance player, JsonObject task)
	{
		int need = ((task["Param"] is JsonArray { Count: >0 } jsonArray) ? TaskProgressService.NumValue(jsonArray[0]) : 0);
		return (need <= 0) ? player.InventoryManager.InventoryData.Weapons.Values.Count : Enumerable.Count(player.InventoryManager.InventoryData.Weapons.Values, (GameWeaponInfo x) => x.Level >= need);
	}

	private static int CountOrangeCharacters(PlayerInstance player)
	{
		Dictionary<ulong, uint> colors = LoadCardColors();
		return Enumerable.Count(player.CharacterManager.CharacterData.Characters, (CharacterInfo x) => colors.TryGetValue(x.TemplateId, out var value) && value >= 5);
	}

	private static Dictionary<ulong, uint> LoadCardColors()
	{
		Dictionary<ulong, uint> dictionary = new Dictionary<ulong, uint>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "item";
			buffer[3] = "templates";
			buffer[4] = "card.json";
			string path = Path.Combine(buffer);
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray != null)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject row)
					{
						uint num = (uint)TaskProgressService.Num(row, "Genre");
						uint detail = (uint)TaskProgressService.Num(row, "Detail");
						uint particular = (uint)TaskProgressService.Num(row, "Particular");
						uint num2 = (uint)TaskProgressService.Num(row, "Level");
						uint value = (uint)TaskProgressService.Num(row, "Color");
						if (num != 0 && num2 != 0)
						{
							dictionary[GameResourceTemplateId.FromGdpl(num, detail, particular, num2)] = value;
						}
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static int CountSupport(PlayerInstance player, JsonObject task)
	{
		int need = ((task["Param"] is JsonArray { Count: >0 } jsonArray) ? TaskProgressService.NumValue(jsonArray[0]) : 0);
		return Enumerable.Count(player.InventoryManager.InventoryData.SupportCards.Values, (GameSupportCardInfo x) => x.Level >= need);
	}

	private static int TotalClips(PlayerInstance player)
	{
		int num = 0;
		for (int i = 0; i < 32; i++)
		{
			num += (int)player.GetAttr(101u, (uint)(3030 + i));
		}
		return num;
	}

	private static int CountFavor20(PlayerInstance player)
	{
		int num = 0;
		for (int i = 1; i <= 50; i++)
		{
			uint attr = player.GetAttr(101u, (uint)(i * 50));
			if ((attr & 0x7FF) >= 20)
			{
				num++;
			}
		}
		return num;
	}

	private static int CountFurniture(PlayerInstance player)
	{
		int num = 0;
		for (int i = 1; i <= 50; i++)
		{
			uint sid = (uint)(i * 50 + 10);
			uint attr = player.GetAttr(101u, sid);
			for (int j = 0; j < 10; j++)
			{
				if (((attr >> j * 3) & 7) != 0)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static int CountRooms(PlayerInstance player)
	{
		int num = 0;
		for (int i = 1; i <= 50; i++)
		{
			uint attr = player.GetAttr(101u, (uint)(i * 50 + 1));
			if (attr >= 1 && attr < 100)
			{
				num++;
			}
		}
		return num;
	}

	private static bool HasPassedChapter(PlayerInstance player, JsonObject task)
	{
		if (!(task["Param"] is JsonArray { Count: >=2 } jsonArray) || !(jsonArray[1] is JsonArray source))
		{
			return false;
		}
		return source.Any((JsonNode x) => uint.TryParse(x?.ToString(), out var result) && player.GetAttr(22u, result) != 0);
	}

	private static void Add(PlayerInstance player, int id, int need, int amount)
	{
		// 第 415 节：必须走 Attributes.GetOrCreate —— 它维护 (Gid,Sid)->PlayerAttr 的字典缓存，
		// 直接 player.Data.Attrs.Add 会让新条目进不了缓存，导致 GetProgress/执行度计算看不到它。
		PlayerAttr playerAttr = player.Attributes.GetOrCreate(2u, (uint)id);
		int value = (int)(playerAttr.Val >> 1) + amount;
		value = Math.Clamp(value, 0, need);
		playerAttr.Val = (playerAttr.Val & 1) | (uint)(value << 1);
	}

	private static bool ParamHasLevel(JsonObject task, uint levelId)
	{
		if (!(task["Param"] is JsonArray source))
		{
			return false;
		}
		return source.Any((JsonNode x) => uint.TryParse(x?.ToString(), out var result) && result == levelId);
	}

	private static bool ContainsLevel(JsonObject task, uint levelId)
	{
		if (!(task["Param"] is JsonArray { Count: >=2 } jsonArray) || !(jsonArray[1] is JsonArray source))
		{
			return false;
		}
		return source.Any((JsonNode x) => uint.TryParse(x?.ToString(), out var result) && result == levelId);
	}

	/// <summary>
	/// 第 415 节：要跟着玩家行为推进的组号 —— 1=日常、3=主线、4=目标。
	/// （2=定期/周常 的事件源还没接，先不动；5=章节总奖励不在这里算。）
	/// </summary>
	private static bool TracksProgress(int group)
	{
		return group == DailyGroup || (uint)(group - 3) <= 1u;
	}

	/// <summary>
	/// 第 415 节：把日常任务的进度和执行度推给在线客户端。
	/// 以前只有 SyncType 会把个别类型的进度塞进回包，日常页基本收不到变更，
	/// 现在每次事件落库后统一推一遍（13 条属性，很便宜）。
	/// </summary>
	private static void PushDaily(PlayerInstance player)
	{
		try
		{
			if (player.Connection == null)
			{
				return;
			}
			NtfSyncPlayer sync = new NtfSyncPlayer();
			bool flag = false;
			foreach (JsonObject item in TaskProgressService.LoadTasks())
			{
				if (TaskProgressService.Num(item, "Group") != DailyGroup)
				{
					continue;
				}
				int id = TaskProgressService.Num(item, "ID");
				PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 2 && x.Sid == (uint)id);
				if (playerAttr != null)
				{
					sync.Custom[player.ToPackedAttrKey(2u, (uint)id)] = playerAttr.Val;
					sync.Custom[player.ToShiftedAttrKey(2u, (uint)id)] = playerAttr.Val;
					flag = true;
				}
			}
			DailyPointService.Recompute(player, sync);
			if (flag)
			{
				_ = player.SendPacket(1004, sync);
			}
		}
		catch
		{
		}
	}

	/// <summary>第 415 节：外部系统（GM 命令等）改完进度后统一落库 + 推送。</summary>
	public static void Flush(PlayerInstance player)
	{
		Save(player);
	}

	private static void Save(PlayerInstance player)
	{
		// 第 415 节：先算执行度（会写 8/1），再落库，否则 8/1 的更新留在内存里没进库。
		PushDaily(player);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
	}
}
