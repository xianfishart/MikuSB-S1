using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 2026-11-13 新增（修复记录第 283 节）：补发「已赠送但没有加好感」的礼物好感。
///
/// 背景：旧版 HouseGiveGift 存在两个 bug（GiftId 取错、把 AreaId 当少女 ID，且只处理 AddLoveNum），
/// 于是礼物被正常消耗、房间里的家具位（support_furniture.json 的 3 bit 槽）也被正常置位，
/// 但好感一点没加。由于客户端判断「该礼物已赠送」用的就是
/// HouseFurniture.CheckFurnitureById(areaId, furnitureTmpId)（见 GetGiftGotInfo 反汇编），
/// 这些礼物在界面上会永久显示「已赠送」，无法再送一次 → 好感永远补不回来。
///
/// 本类按「已置位的家具槽」反推应得好感并一次性补发，用 Config/Database/giftfavor_&lt;uid&gt;.json
/// 记录已补发过的 (区域:礼物) 组合，保证幂等，重复调用不会重复加。
/// </summary>
public static class HouseGiftRepair
{
	/// <summary>礼物家具模板 ID（= gift.json 的 GiftId = dorm_gift.json 的 Param1）→ (角色好感, 氛围值)。</summary>
	private static Dictionary<int, (uint Love, uint House)>? _favor;

	/// <summary>家具摆放表：区域 + 槽位序号 + 家具模板 ID。</summary>
	private static List<(int AreaId, int Index, int TmpId)>? _slots;

	/// <summary>礼物家具模板 ID 集合（只有礼物才补好感，普通家具跳过）。</summary>
	private static HashSet<int>? _giftTmpIds;

	/// <summary>
	/// 扫描所有已置位的礼物槽位，把还没补过的好感加回去。
	/// 返回值：(补发件数, 角色好感合计, 氛围值合计, 涉及少女数)。
	/// </summary>
	public static async Task<(int Applied, uint Love, uint House, int Girls)> RepairAsync(Connection connection, NtfSyncPlayer sync)
	{
		PlayerInstance? player = connection?.Player;
		if (player == null)
		{
			return (Applied: 0, Love: 0u, House: 0u, Girls: 0);
		}
		EnsureTables();
		if (_favor == null || _slots == null || _giftTmpIds == null || _slots.Count == 0)
		{
			return (Applied: 0, Love: 0u, House: 0u, Girls: 0);
		}
		HashSet<string> done = LoadDone(player.Uid);
		int applied = 0;
		uint loveTotal = 0u;
		uint houseTotal = 0u;
		HashSet<int> girls = new HashSet<int>();
		foreach ((int AreaId, int Index, int TmpId) slot in _slots)
		{
			if (!_giftTmpIds.Contains(slot.TmpId))
			{
				continue;
			}
			if (!_favor.TryGetValue(slot.TmpId, out (uint, uint) cfg) || (cfg.Item1 == 0 && cfg.Item2 == 0))
			{
				continue;
			}
			(uint Sid, int Shift) fur = HouseGiveGift.FurSlot(slot.AreaId, slot.Index);
			uint value = HouseAttr.Read(player, fur.Sid);
			if (((value >> fur.Shift) & 7) == 0)
			{
				continue;
			}
			string key = slot.AreaId + ":" + slot.TmpId;
			if (done.Contains(key))
			{
				continue;
			}
			int girlId = HouseAttr.GirlInArea(player, slot.AreaId);
			if (cfg.Item1 != 0 && girlId <= 0)
			{
				// 找不到对应少女（公共区域多人 / 空房）：先不补，等以后能定位再补，不写标记。
				continue;
			}
			if (cfg.Item1 != 0)
			{
				await HouseFavor.Award(connection, girlId, cfg.Item1, sync);
				loveTotal += cfg.Item1;
				girls.Add(girlId);
			}
			if (cfg.Item2 != 0)
			{
				await HouseFavor.AwardHouse(connection, cfg.Item2, sync);
				houseTotal += cfg.Item2;
			}
			done.Add(key);
			applied++;
		}
		if (applied > 0)
		{
			SaveDone(player.Uid, done);
		}
		return (Applied: applied, Love: loveTotal, House: houseTotal, Girls: girls.Count);
	}

	private static void EnsureTables()
	{
		if (_favor != null)
		{
			return;
		}
		_favor = new Dictionary<int, (uint, uint)>();
		_giftTmpIds = new HashSet<int>();
		_slots = new List<(int, int, int)>();
		try
		{
			string cwd = Directory.GetCurrentDirectory();
			string giftPath = Path.Combine(cwd, "Resources", "house", "gift.json");
			if (File.Exists(giftPath) && JsonNode.Parse(File.ReadAllText(giftPath)) is JsonArray giftArray)
			{
				foreach (JsonNode node in giftArray)
				{
					if (node is JsonObject obj)
					{
						int giftId = HouseJson.NumField(obj, "GiftId");
						if (giftId > 0)
						{
							_favor[giftId] = (ToUInt(obj["AddLoveNum"]), ToUInt(obj["AddHouseLoveNum"]));
						}
					}
				}
			}
			string dormGiftPath = Path.Combine(cwd, "Resources", "item", "templates", "dorm_gift.json");
			if (File.Exists(dormGiftPath) && JsonNode.Parse(File.ReadAllText(dormGiftPath)) is JsonArray dormArray)
			{
				foreach (JsonNode node2 in dormArray)
				{
					if (node2 is JsonObject obj2)
					{
						int param = HouseJson.NumField(obj2, "Param1");
						if (param > 0)
						{
							_giftTmpIds.Add(param);
						}
					}
				}
			}
			string supportPath = Path.Combine(cwd, "Resources", "house", "support_furniture.json");
			if (File.Exists(supportPath) && JsonNode.Parse(File.ReadAllText(supportPath)) is JsonArray supportArray)
			{
				foreach (JsonNode node3 in supportArray)
				{
					if (node3 is JsonObject obj3)
					{
						int areaId = HouseJson.NumField(obj3, "AreaId");
						int index = HouseJson.NumField(obj3, "Index");
						int tmpId = HouseJson.NumField(obj3, "FurnitureTmpId");
						if (areaId > 0 && tmpId > 0 && index >= 0)
						{
							_slots.Add((areaId, index, tmpId));
						}
					}
				}
			}
		}
		catch
		{
		}
	}

	private static uint ToUInt(JsonNode? node)
	{
		if (node == null)
		{
			return 0u;
		}
		if (uint.TryParse(node.ToString(), out var value))
		{
			return value;
		}
		return 0u;
	}

	private static string MarkerPath(int uid)
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "Config", "Database", "giftfavor_" + uid + ".json");
	}

	private static HashSet<string> LoadDone(int uid)
	{
		HashSet<string> set = new HashSet<string>();
		try
		{
			string path = MarkerPath(uid);
			if (!File.Exists(path))
			{
				return set;
			}
			if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject obj)
			{
				foreach (KeyValuePair<string, JsonNode?> item in obj)
				{
					set.Add(item.Key);
				}
			}
		}
		catch
		{
		}
		return set;
	}

	private static void SaveDone(int uid, HashSet<string> done)
	{
		try
		{
			JsonObject obj = new JsonObject();
			foreach (string key in done.OrderBy((string x) => x, StringComparer.Ordinal))
			{
				obj[key] = true;
			}
			File.WriteAllText(MarkerPath(uid), obj.ToJsonString());
		}
		catch
		{
		}
	}
}
