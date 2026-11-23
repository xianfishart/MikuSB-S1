using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_UpdateLevel")]
public class GirlCard_UpdateLevel : ICallGSHandler
{
	private const uint CashGroupId = 1u;

	private const uint SilverMoneyType = 3u;

	private const uint SilverSid = 7u;

	private const uint RoleMaxLevel = 80u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		GirlCardUpdateLevelParam req = JsonSerializer.Deserialize<GirlCardUpdateLevelParam>(param);
		if (req == null || req.Id == 0 || req.Materials == null || req.Materials.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.Id) ?? player.CharacterManager.CharacterData.Characters.FirstOrDefault((CharacterInfo x) => x.TemplateId == (ulong)req.Id) ?? player.CharacterManager.CharacterData.Characters.FirstOrDefault();
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CardExcel cardTemplate = GameData.CardData.Values.FirstOrDefault((CardExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == card.TemplateId);
		if (cardTemplate == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint levelCap = GetCardLevelCap(player.Data.Level, cardTemplate.LevelLimitID);
		if (levelCap == 0)
		{
			levelCap = card.Level;
		}
		if (card.Level >= 80)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "null", new NtfSyncPlayer
			{
				Items = { card.ToProto() }
			});
			return;
		}
		Dictionary<uint, uint> requestedMaterials = new Dictionary<uint, uint>();
		foreach (GirlCardLevelMaterial row in req.Materials)
		{
			if (row != null && row.Id != 0 && row.Num != 0)
			{
				requestedMaterials[(uint)row.Id] = requestedMaterials.GetValueOrDefault((uint)row.Id) + row.Num;
			}
		}
		if (requestedMaterials.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"tip.material_not_enough\"}");
			return;
		}
		ulong totalExp = 0uL;
		ulong totalSilverCost = 0uL;
		uint value;
		uint key;
		foreach (KeyValuePair<uint, uint> item3 in requestedMaterials)
		{
			item3.Deconstruct(out value, out key);
			uint itemId = value;
			uint count = key;
			BaseGameItemInfo item = player.InventoryManager.GetNormalItem(itemId);
			if (item == null || item.ItemCount < count)
			{
				await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"tip.material_not_enough\"}");
				return;
			}
			if (!GameData.SuppliesByFullId.TryGetValue(item.TemplateId, out SuppliesExcel supplies) || supplies.ProvideExp == 0)
			{
				await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			totalExp += (ulong)((long)supplies.ProvideExp * (long)count);
			totalSilverCost += (ulong)((long)supplies.ConsumeGold * (long)count);
			supplies = null;
		}
		PlayerAttr silverAttr = GetOrCreateAttr(player.Data, 1u, 7u);
		if (silverAttr.Val < totalSilverCost)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "{\"sErr\":\"tip.material_not_enough\"}");
			return;
		}
		List<Item> syncItems = new List<Item>();
		foreach (KeyValuePair<uint, uint> item4 in requestedMaterials)
		{
			item4.Deconstruct(out key, out value);
			uint itemId2 = key;
			uint count2 = value;
			BaseGameItemInfo item2 = player.InventoryManager.GetNormalItem(itemId2);
			item2.ItemCount -= count2;
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				syncItems.Add(BuildRemovedProto(item2));
			}
			else
			{
				syncItems.Add(item2.ToProto());
			}
		}
		silverAttr.Val -= checked((uint)totalSilverCost);
		var (newLevel, newExp) = ApplyCardExp(card.Level, card.Exp, totalExp, levelCap);
		card.Level = newLevel;
		card.Exp = checked((int)newExp);
		syncItems.Add(card.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		sync.Custom[player.ToPackedAttrKey(1u, 7u)] = silverAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(1u, 7u)] = silverAttr.Val;
		await CallGSRouter.SendScript(connection, "GirlCard_UpdateLevel", "null", sync);
	}

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid,
			Val = 0u
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static Item BuildRemovedProto(BaseGameItemInfo item)
	{
		Item item2 = item.ToProto();
		item2.Count = 0u;
		return item2;
	}

	private static uint GetCardLevelCap(uint playerLevel, int levelLimitId)
	{
		List<(uint, uint)> list = LoadCardLevelLimit(levelLimitId);
		if (list.Count == 0)
		{
			return 0u;
		}
		uint num = 0u;
		uint num2 = 0u;
		foreach (var (num3, num4) in list)
		{
			if (num3 < playerLevel)
			{
				num = num3;
				num2 = num4;
				continue;
			}
			if (num3 == playerLevel)
			{
				return Math.Min(num4, 80u);
			}
			uint num5 = num3 - num;
			if (num5 == 0)
			{
				return Math.Min(num4, 80u);
			}
			double num6 = (double)(playerLevel - num) / (double)num5;
			uint val = (uint)Math.Floor((double)num2 + (double)(num4 - num2) * num6);
			return Math.Min(val, 80u);
		}
		return Math.Min(num2, 80u);
	}

	private static List<(uint AccountLevel, uint CardLevel)> LoadCardLevelLimit(int levelLimitId)
	{
		string path = Path.Combine(AppContext.BaseDirectory, "Resources", "item", "level_limit.json");
		if (!File.Exists(path))
		{
			return new List<(uint, uint)>();
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
		List<(uint, uint)> list = new List<(uint, uint)>();
		foreach (JsonElement item2 in jsonDocument.RootElement.EnumerateArray())
		{
			if (!item2.TryGetProperty("ID", out var value) || value.GetInt32() != levelLimitId || !item2.TryGetProperty("Type", out var value2) || value2.GetInt32() != 1 || !item2.TryGetProperty("Limit", out var value3) || value3.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			foreach (JsonProperty item3 in value3.EnumerateObject())
			{
				if (!uint.TryParse(item3.Name, out var result))
				{
					continue;
				}
				uint item;
				if (item3.Value.ValueKind == JsonValueKind.Number)
				{
					item = item3.Value.GetUInt32();
				}
				else
				{
					if (item3.Value.ValueKind != JsonValueKind.String || !uint.TryParse(item3.Value.GetString(), out var result2))
					{
						continue;
					}
					item = result2;
				}
				list.Add((result, item));
			}
			break;
		}
		list.Sort(((uint AccountLevel, uint CardLevel) a, (uint AccountLevel, uint CardLevel) b) => a.AccountLevel.CompareTo(b.AccountLevel));
		return list;
	}

	private static (uint Level, ulong Exp) ApplyCardExp(uint level, int currentExp, ulong addedExp, uint levelCap)
	{
		uint num = ((level == 0) ? 1u : level);
		ulong num2 = (ulong)Math.Max(0, currentExp) + addedExp;
		if (levelCap != 0 && num >= levelCap)
		{
			return (Level: num, Exp: num2);
		}
		while (num < 80)
		{
			uint cardNeedExp = GetCardNeedExp(num);
			if (cardNeedExp == 0 || num2 < cardNeedExp)
			{
				break;
			}
			num2 -= cardNeedExp;
			num++;
			if (levelCap != 0 && num >= levelCap)
			{
				return (Level: levelCap, Exp: num2);
			}
		}
		return (Level: num, Exp: num2);
	}

	private static uint GetCardNeedExp(uint currentLevel)
	{
		if (GameData.UpgradeExpData.TryGetValue((int)currentLevel, out UpgradeExpExcel value))
		{
			return value.CardNeedExp;
		}
		return 0u;
	}
}
