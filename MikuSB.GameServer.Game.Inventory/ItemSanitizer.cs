using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Game.Inventory;

internal static class ItemSanitizer
{
	private static HashSet<(uint G, uint D, uint P, uint L)>? _banned;

	// 2026-11-15（修复记录第 348 节）：4-99-1-1 是官方表里的「未启用占位行」，
	// 对照官方 3.6.0 资源包（PAK_Game_Settings_0 → riki/Riki.txt、all_language/ui/riki.txt）：
	//   · Riki.txt 共 1647 行，Type 13（纪念信物）从 13002 起，没有 13001；
	//   · riki.txt 的 mail 词条是 mail002 ~ mail081，没有 mail001_title/_content；
	//   · 物品表那行的 Icon 是 1510202 —— 正是「能量方晶」（5-13-2-3）的图标，GMnum 还是空。
	// 也就是说官方从未发放过「第 1 封信物」，玩家手里这条只能来自历史全量发放。
	// 按官方身份转成能量方晶，避免背包里留一件官方不存在的信物（客户端只会显示 _title 原始 key）。
	private static readonly ulong TemplateRikiMail001 = GameResourceTemplateId.FromGdpl(4u, 99u, 1u, 1u);
	private static readonly ulong TemplateWeaponModule3 = GameResourceTemplateId.FromGdpl(5u, 13u, 2u, 3u);

	private static readonly string[] Files = new string[9] { "weapon_parts.json", "weapon.json", "support_card.json", "suplies.json", "others.json", "dorm_gift.json", "card_skin.json", "card.json", "profile.json" };

	public static bool IsBanned(uint g, uint d, uint p, uint l)
	{
		return (_banned ?? (_banned = LoadBanned())).Contains((g, d, p, l));
	}

	public static void Cleanup(PlayerInstance player)
	{
		InventoryManager inventoryManager = player.InventoryManager;
		if (inventoryManager?.InventoryData == null)
		{
			return;
		}
		HashSet<(uint, uint, uint, uint)> hashSet = _banned ?? (_banned = LoadBanned());
		// 注意：不能因为 ban 列表为空就提前 return —— 幽灵道具清理不依赖 ban 列表，
		// 本地没有 ban_items.json 时也得跑（第 347 节）。
		int num = 0;
		int num2 = 0;
		// 2026-11-15（修复记录第 347 节）：模板表里查不到的「可使用道具」一律清掉。
		// 这种条目多半来自历史上某条把 ulong 模板 ID 强转 uint 的发放路径（高 32 位被砍，
		// 坐标退化成 4-1-0-0 之类）。客户端拿不到模板就拼不出名字和图标（界面上只剩 _use/_des），
		// 点「使用」还会卡在加载界面。表没加载完时不动手，避免误删。
		bool canCheckGhost = GameData.OtherItemByFullId.Count > 0;
		foreach (BaseGameItemInfo item in inventoryManager.InventoryData.Items.Values.ToList())
		{
			if (item.TemplateId == TemplateRikiMail001)
			{
				// 4-99-1-1 → 能量方晶 5-13-2-3（已有则累加，否则原条目改坐标，UniqueId 不动）。
				// 必须放在幽灵清理之前：others.json 里这一行已经删掉，晚了就会被当成幽灵直接删。
				BaseGameItemInfo? exist = inventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == TemplateWeaponModule3 && x.UniqueId != item.UniqueId);
				if (exist != null)
				{
					exist.ItemCount += item.ItemCount;
					inventoryManager.InventoryData.Items.Remove(item.UniqueId);
				}
				else
				{
					item.TemplateId = TemplateWeaponModule3;
				}
				num2++;
				continue;
			}
			if (item.TemplateId == 262148)
			{
				item.TemplateId = 281479271940100uL;
				num2++;
			}
			else if (hashSet.Contains(Decode(item.TemplateId)))
			{
				inventoryManager.InventoryData.Items.Remove(item.UniqueId);
				num++;
			}
			else if (canCheckGhost && item.ItemType == ItemTypeEnum.TYPE_USEABLE && !GameData.OtherItemByFullId.ContainsKey(item.TemplateId))
			{
				inventoryManager.InventoryData.Items.Remove(item.UniqueId);
				num++;
			}
		}
		foreach (GameWeaponInfo item2 in inventoryManager.InventoryData.Weapons.Values.ToList())
		{
			if (hashSet.Contains(Decode(item2.TemplateId)))
			{
				inventoryManager.InventoryData.Weapons.Remove(item2.UniqueId);
				num++;
			}
		}
		foreach (GameSkinInfo item3 in inventoryManager.InventoryData.Skins.Values.ToList())
		{
			if (hashSet.Contains(Decode(item3.TemplateId)))
			{
				inventoryManager.InventoryData.Skins.Remove(item3.UniqueId);
				num++;
			}
		}
		foreach (GameSupportCardInfo item4 in inventoryManager.InventoryData.SupportCards.Values.ToList())
		{
			if (hashSet.Contains(Decode(item4.TemplateId)))
			{
				inventoryManager.InventoryData.SupportCards.Remove(item4.UniqueId);
				num++;
			}
		}
		if (num <= 0 && num2 <= 0)
		{
			return;
		}
		DatabaseHelper.SaveDatabaseType(inventoryManager.InventoryData);
		try
		{
			File.AppendAllText("D:\\DSH\\.tools\\item_cleanup.log", $"{DateTime.Now:HH:mm:ss} uid={player.Uid} removed={num} fixed={num2}\n");
		}
		catch
		{
		}
	}

	private static (uint, uint, uint, uint) Decode(ulong templateId)
	{
		return ((uint)(templateId & 0xFFFF), (uint)((templateId >> 16) & 0xFFFF), (uint)((templateId >> 32) & 0xFFFF), (uint)((templateId >> 48) & 0xFFFF));
	}

	private static HashSet<(uint, uint, uint, uint)> LoadBanned()
	{
		HashSet<(uint, uint, uint, uint)> hashSet = new HashSet<(uint, uint, uint, uint)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "item", "ban_items.json");
			if (File.Exists(path))
			{
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode item in jsonArray)
					{
						string[] array = item?.ToString().Split('-');
						if (array != null && array.Length == 4 && uint.TryParse(array[0], out var result) && uint.TryParse(array[1], out var result2) && uint.TryParse(array[2], out var result3) && uint.TryParse(array[3], out var result4))
						{
							hashSet.Add((result, result2, result3, result4));
						}
					}
				}
			}
		}
		catch
		{
		}
		string path2 = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "item", "templates");
		string[] files = Files;
		foreach (string path3 in files)
		{
			string path4 = Path.Combine(path2, path3);
			if (!File.Exists(path4))
			{
				continue;
			}
			try
			{
				JsonArray jsonArray2 = JsonNode.Parse(File.ReadAllText(path4))?.AsArray();
				if (jsonArray2 == null)
				{
					continue;
				}
				foreach (JsonNode item2 in jsonArray2)
				{
					if (!(item2 is JsonObject jsonObject))
					{
						continue;
					}
					bool flag;
					switch (jsonObject["Close"]?.ToString() ?? "")
					{
					case "1":
					case "True":
					case "true":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					if (flag)
					{
						(uint, uint, uint, uint)? tuple = Key(jsonObject);
						if (tuple.HasValue)
						{
							hashSet.Add(tuple.Value);
						}
					}
				}
			}
			catch
			{
			}
		}
		return hashSet;
	}

	private static (uint, uint, uint, uint)? Key(JsonObject row)
	{
		try
		{
			uint num = (uint)(row["Genre"]?.GetValue<int>() ?? 0);
			uint num2 = (uint)(row["Detail"]?.GetValue<int>() ?? 0);
			uint num3 = (uint)(row["Particular"]?.GetValue<int>() ?? 0);
			uint num4 = (uint)(row["Level"]?.GetValue<int>() ?? 0);
			if (num == 0 && num2 == 0 && num3 == 0 && num4 == 0)
			{
				return null;
			}
			return (num, num2, num3, num4);
		}
		catch
		{
			return null;
		}
	}
}
