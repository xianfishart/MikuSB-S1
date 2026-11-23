using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.GameServer.Server.CallGS.Handlers.Activity;
using MikuSB.GameServer.Server.CallGS.Handlers.House;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Reward;

public sealed class RewardManager : BasePlayerManager
{
	public readonly record struct RewardEntry(uint Genre, uint Detail, uint Particular, uint Level, uint Count)
	{
		public JsonArray ToJson()
		{
			InlineArray5<JsonNode> buffer = default;
			buffer[0] = (int)Genre;
			buffer[1] = (int)Detail;
			buffer[2] = (int)Particular;
			buffer[3] = (int)Level;
			buffer[4] = (int)Count;
			return new JsonArray(buffer);
		}

		public uint[] ToArray()
		{
			return new uint[5] { Genre, Detail, Particular, Level, Count };
		}
	}

	private static readonly Logger Logger = new Logger("Reward");

	public RewardManager(PlayerInstance player)
		: base(player)
	{
	}

	public async ValueTask<JsonArray> GrantLevelRewardsAsync(ILevelRewardConfig levelConfig, bool isFirstClear, uint seed, NtfSyncPlayer sync, uint lineupId = 0u, bool grantExperience = true)
	{
		List<RewardEntry> entries = ResolveLevelRewards(levelConfig, isFirstClear, seed);
		Logger.Info($"level settlement uid={Player.Uid} level={levelConfig.ID} table={levelConfig.GetType().Name} firstClear={isFirstClear} dropRows={entries.Count} exp={levelConfig.PlayerExp()}/{levelConfig.RoleExp()}");
		uint vigorCost = levelConfig.ConsumeVigor();
		if (vigorCost != 0)
		{
			try
			{
				TaskEventService.AddByType(Player, 4, (int)vigorCost);
			}
			catch
			{
			}
		}
		JsonArray response = new JsonArray();
		foreach (RewardEntry entry in entries)
		{
			if (await GrantRewardAsync(entry, sync))
			{
				response.Add(entry.ToJson());
			}
		}
		if (!grantExperience)
		{
			return response;
		}
		uint playerExp = levelConfig.PlayerExp();
		if (playerExp != 0 && Player.Data.Level < 60)
		{
			playerExp = playerExp * 3 / 2;
		}
		IReadOnlyList<PlayerLevelExcel> leveledUp = Player.AddPlayerExperience(playerExp, sync);
		foreach (PlayerLevelExcel level in leveledUp)
		{
			foreach (IReadOnlyList<uint> gift in level.GiftItems)
			{
				if (gift.Count >= 5)
				{
					await GrantRewardAsync(new RewardEntry(gift[0], gift[1], gift[2], gift[3], gift[4]), sync, grantLevelRewards: false);
				}
			}
		}
		if (leveledUp.Count > 0)
		{
			SevenDayProgress.EnsurePlayerLevelTasks(Player);
		}
		int gainedLevels = Player.CharacterManager.AddExperienceToAllLineups(levelConfig.RoleExp(), sync);
		if (gainedLevels > 0)
		{
			TaskEventService.OnCharacterLevelUp(Player, gainedLevels);
		}
		return response;
	}

	public List<RewardEntry> ResolveLevelRewards(ILevelRewardConfig levelConfig, bool isFirstClear, uint seed)
	{
		Random random = new Random((int)(seed ^ levelConfig.ID));
		List<RewardEntry> list = new List<RewardEntry>();
		ResolveDropIds(levelConfig.FirstDropIds(), isFirstClear, random, list);
		ResolveDropIds(levelConfig.BaseDropIds(), !isFirstClear, random, list);
		ResolveDropIds(levelConfig.RandomDropIds(), enabled: true, random, list);
		return list;
	}

	public async ValueTask<JsonArray> GrantConfiguredRewardsAsync(IEnumerable<IReadOnlyList<uint>> rewardRows, NtfSyncPlayer sync)
	{
		JsonArray response = new JsonArray();
		foreach (IReadOnlyList<uint> row in rewardRows)
		{
			if (row.Count >= 5 && row[4] != 0)
			{
				RewardEntry entry = new RewardEntry(row[0], row[1], row[2], row[3], row[4]);
				if (await GrantRewardAsync(entry, sync))
				{
					response.Add(entry.ToJson());
				}
			}
		}
		return response;
	}

	public ValueTask<bool> GrantRewardAsync(IReadOnlyList<uint> reward, NtfSyncPlayer sync)
	{
		if (reward.Count < 5)
		{
			return ValueTask.FromResult(result: false);
		}
		return GrantRewardAsync(new RewardEntry(reward[0], reward[1], reward[2], reward[3], reward[4]), sync);
	}

	public async ValueTask<bool> GrantRewardAsync(RewardEntry reward, NtfSyncPlayer sync, bool grantLevelRewards = true)
	{
		if (reward.Count == 0 || reward.Genre == 0)
		{
			return false;
		}
		ItemTypeEnum itemType = (ItemTypeEnum)reward.Genre;
		uint count = Math.Max(1u, reward.Count);
		uint detail = reward.Detail;
		uint particular = reward.Particular;
		uint level = reward.Level;
		switch (itemType)
		{
		case ItemTypeEnum.TYPE_CARD:
		{
			for (uint i5 = 0u; i5 < count; i5++)
			{
				CharacterInfo character = await Player.CharacterManager.AddCharacter(itemType, detail, particular, level, 1u, sendPacket: false);
				if (character != null)
				{
					sync.Items.Add(character.ToProto());
				}
			}
			return true;
		}
		case ItemTypeEnum.TYPE_WEAPON:
		{
			for (uint i2 = 0u; i2 < count; i2++)
			{
				GameWeaponInfo weapon = await Player.InventoryManager.AddWeaponItem(itemType, detail, particular, level, 1u, sendPacket: false);
				if (weapon != null)
				{
					sync.Items.Add(weapon.ToProto());
				}
			}
			return true;
		}
		case ItemTypeEnum.TYPE_SUPPORT:
		{
			for (uint i4 = 0u; i4 < count; i4++)
			{
				GameSupportCardInfo support = await Player.InventoryManager.AddSupportCardItem(detail, particular, level, 1u, sendPacket: false);
				if (support != null)
				{
					sync.Items.Add(support.ToProto());
				}
			}
			return true;
		}
		case ItemTypeEnum.TYPE_SUPPLIES:
		{
			SuppliesExcel supplies = FindSupplies(reward.Genre, detail, particular, level);
			if (supplies == null)
			{
				Logger.Warn($"Supplies config not found. gdpl={reward.Genre},{detail},{particular},{level} count={count}");
				return false;
			}
			BaseGameItemInfo item2 = await Player.InventoryManager.AddSuppliesItem(supplies, count, sendPacket: false);
			if (item2 != null)
			{
				sync.Items.Add(item2.ToProto());
			}
			return item2 != null;
		}
		case ItemTypeEnum.TYPE_USEABLE:
			return await GrantUseableAsync(reward, sync, grantLevelRewards);
		case ItemTypeEnum.TYPE_WEAPON_PART:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddWeaponPartItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_CARD_SKIN:
		{
			bool granted2 = false;
			for (uint i3 = 0u; i3 < count; i3++)
			{
				GameSkinInfo item3 = await Player.InventoryManager.AddSkinItem(itemType, detail, particular, level, sendPacket: false);
				if (item3 != null)
				{
					granted2 = true;
					sync.Items.Add(item3.ToProto());
				}
			}
			return granted2;
		}
		case ItemTypeEnum.TYPE_HOUSE:
		{
			bool granted = false;
			for (uint i = 0u; i < count; i++)
			{
				BaseGameItemInfo item = await Player.InventoryManager.AddHouseFurnitureItem(itemType, detail, particular, level, sendPacket: false);
				if (item != null)
				{
					granted = true;
					sync.Items.Add(item.ToProto());
					// 2026-11-13（修复记录第 287 节）：这里原本会调 HouseGiveGift.MarkCollectedForPlayer 把
					// 「获得家具」当成「已赠送给少女」，导致礼物一买进背包就自动摆进房间、赠送界面直接变灰。
					// 礼物只应入背包，是否赠送由玩家在赠送界面点「赠送」触发（HouseGiveGift.Handle 的
					// MarkFurnitureCollected 才是唯一合法的置位点），故移除该调用。
				}
			}
			return granted;
		}
		case ItemTypeEnum.TYPE_PROFILE:
		case ItemTypeEnum.TYPE_FRAME:
		case ItemTypeEnum.TYPE_BADGE:
		case ItemTypeEnum.TYPE_COVER:
		case ItemTypeEnum.TYPE_NAMECARD:
		case ItemTypeEnum.TYPE_EXPRESSION:
		case ItemTypeEnum.TYPE_BUBBLE:
		case ItemTypeEnum.TYPE_ANALYST:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddProfileItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_WEAPON_SKIN:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddWeaponSkinItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_MANIFESTATION:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddManifestationItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_CARD_SKIN_PART:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddSkinPartItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_AR:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddArItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_CALL:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddCallItem(g, d, p, l, sendPacket: false));
		case ItemTypeEnum.TYPE_MONSTER_CARD:
			return await AddRepeatedAsync(reward, sync, (ItemTypeEnum g, uint d, uint p, uint l) => Player.InventoryManager.AddMonsterCardItem(d, p, l, sendPacket: false));
		default:
			Logger.Warn($"Unsupported reward item type. genre={reward.Genre} detail={detail} particular={particular} level={level}");
			return false;
		}
	}

	private async ValueTask<bool> GrantUseableAsync(RewardEntry reward, NtfSyncPlayer sync, bool grantLevelRewards)
	{
		OtherItemExcel otherItem = FindOtherItem(reward.Genre, reward.Detail, reward.Particular, reward.Level);
		checked
		{
			if (otherItem != null)
			{
				switch (otherItem.LuaType)
				{
				case "cashbox":
				{
					// 第 439 节：cashbox 的 Param1 是「货币类型 id」（cash 表 Id，例如 61 = 星祈心愿的
					// 心愿能量 starwish.CoinName1），不是「每个盒子装多少」。原实现 goto 到 money_box
					// 分支，把 Param1 当倍率乘上数量：[4,1,36,1,30] → 61×30 = 1830，
					// 星祈心愿三档 30/30/40 合计被发成 6100 心愿能量。
					if (otherItem.Param1 == 0)
					{
						break;
					}
					Player.AddCurrency(otherItem.Param1, reward.Count, sync);
					return true;
				}
				case "money_box":
				case "gold_box":
				case "silver_box":
				case "vigor_box":
				{
					string luaType = otherItem.LuaType;
					if (1 == 0)
					{
					}
					uint num = luaType switch
					{
						"money_box" => 1u, 
						"gold_box" => 2u, 
						"silver_box" => 3u, 
						"vigor_box" => 4u, 
						_ => 0u, 
					};
					if (1 == 0)
					{
					}
					uint moneyType = num;
					if (moneyType == 0)
					{
						moneyType = otherItem.Param1;
					}
					Player.AddCurrency(moneyType, otherItem.Param1 * reward.Count, sync);
					return true;
				}
				case "playerexp_box":
				{
					IReadOnlyList<PlayerLevelExcel> leveledUp = Player.AddPlayerExperience(otherItem.Param1 * reward.Count, sync);
					if (grantLevelRewards)
					{
						foreach (PlayerLevelExcel level in leveledUp)
						{
							foreach (IReadOnlyList<uint> gift in level.GiftItems)
							{
								if (gift.Count >= 5)
								{
									await GrantRewardAsync(new RewardEntry(gift[0], gift[1], gift[2], gift[3], gift[4]), sync, grantLevelRewards: false);
								}
							}
						}
					}
					return true;
				}
				}
			}
			BaseGameItemInfo item = AddOtherItem(otherItem, reward);
			if (item == null)
			{
				return false;
			}
			sync.Items.Add(item.ToProto());
			return true;
		}
	}

	private async ValueTask<bool> AddRepeatedAsync(RewardEntry reward, NtfSyncPlayer sync, Func<ItemTypeEnum, uint, uint, uint, ValueTask<BaseGameItemInfo?>> addItem)
	{
		bool granted = false;
		for (uint i = 0u; i < reward.Count; i++)
		{
			BaseGameItemInfo item = await addItem((ItemTypeEnum)reward.Genre, reward.Detail, reward.Particular, reward.Level);
			if (item != null)
			{
				granted = true;
				sync.Items.Add(item.ToProto());
			}
		}
		return granted;
	}

	private void ResolveDropIds(IReadOnlyList<uint> dropIds, bool enabled, Random random, ICollection<RewardEntry> rewards)
	{
		if (!enabled)
		{
			return;
		}
		foreach (uint dropId in dropIds)
		{
			if (!GameData.DropData.TryGetValue(dropId, out DropExcel value))
			{
				Logger.Warn($"Drop config not found. dropId={dropId}");
				continue;
			}
			foreach (List<uint> item in value.Drop)
			{
				if (item.Count >= 3 && item[0] != 0 && item[2] != 0)
				{
					if (!GameData.DropGroupData.TryGetValue(item[0], out DropGroupExcel value2))
					{
						Logger.Warn($"Drop group config not found. groupId={item[0]}");
					}
					else
					{
						ResolveDropGroup(value2, item[2], random, rewards);
					}
				}
			}
		}
	}

	private static void ResolveDropGroup(DropGroupExcel groupConfig, uint multiplier, Random random, ICollection<RewardEntry> rewards)
	{
		if (!(groupConfig.GroupRaw is JArray jArray))
		{
			return;
		}
		if (groupConfig.RandType == 1)
		{
			for (uint num = 0u; num < multiplier; num++)
			{
				JArray jArray2 = SelectWeighted(jArray, random);
				if (jArray2 != null && TryReadReward(jArray2, out var reward))
				{
					rewards.Add(reward);
				}
			}
			return;
		}
		if (groupConfig.RandType == 2)
		{
			JArray jArray3 = SelectWeighted(jArray, random);
			if (jArray3 == null || jArray3.Count < 3 || !(jArray3[2] is JArray entries))
			{
				return;
			}
			uint num2 = ReadUInt(jArray3[0]);
			for (uint num3 = 0u; num3 < multiplier * num2; num3++)
			{
				JArray jArray4 = SelectWeighted(entries, random);
				if (jArray4 != null && TryReadReward(jArray4, out var reward2))
				{
					rewards.Add(reward2);
				}
			}
			return;
		}
		foreach (JToken item in jArray)
		{
			if (item is JArray entry && TryReadReward(entry, out var reward3))
			{
				reward3 = reward3 with
				{
					Count = checked(reward3.Count * multiplier)
				};
				rewards.Add(reward3);
			}
		}
	}

	private static JArray? SelectWeighted(JArray entries, Random random)
	{
		JArray[] array = (from entry in entries.OfType<JArray>()
			where entry.Count >= 2 && ReadUInt(entry[1]) != 0
			select entry).ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		ulong num = array.Aggregate(0uL, (ulong total, JArray entry) => total + ReadUInt(entry[1]));
		ulong num2 = (ulong)(random.NextDouble() * (double)num);
		JArray[] array2 = array;
		foreach (JArray jArray in array2)
		{
			uint num4 = ReadUInt(jArray[1]);
			if (num2 < num4)
			{
				return jArray;
			}
			num2 -= num4;
		}
		return array[^1];
	}

	private static bool TryReadReward(JArray entry, out RewardEntry reward)
	{
		reward = default;
		if (entry.Count < 3 || !(entry[0] is JArray { Count: >=4 } jArray))
		{
			return false;
		}
		uint[] array = jArray.Select(ReadUInt).ToArray();
		uint num = ReadUInt(entry[2]);
		if (array.Any((uint x) => x == 0) || num == 0)
		{
			return false;
		}
		reward = new RewardEntry(array[0], array[1], array[2], array[3], num);
		return true;
	}

	private static SuppliesExcel? FindSupplies(uint genre, uint detail, uint particular, uint level)
	{
		ulong key = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
		if (GameData.SuppliesByFullId.TryGetValue(key, out SuppliesExcel value))
		{
			return value;
		}
		return GameData.AllSuppliesData.FirstOrDefault((SuppliesExcel x) => x.Genre == genre && x.Detail == detail && x.Particular == particular && x.Level == level);
	}

	private static OtherItemExcel? FindOtherItem(uint genre, uint detail, uint particular, uint level)
	{
		ulong key = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
		if (GameData.OtherItemByFullId.TryGetValue(key, out OtherItemExcel value))
		{
			return value;
		}
		return GameData.OtherItemData.Values.FirstOrDefault((OtherItemExcel x) => x.Genre == genre && x.Detail == detail && x.Particular == particular && x.Level == level);
	}

	private BaseGameItemInfo? AddOtherItem(OtherItemExcel? otherItem, RewardEntry reward)
	{
		if (otherItem == null)
		{
			return null;
		}
		ulong templateId = GameResourceTemplateId.FromGdpl(reward.Genre, reward.Detail, reward.Particular, reward.Level);
		InventoryData inventoryData = Player.InventoryManager.InventoryData;
		uint val = ((otherItem.GMnum != 0) ? otherItem.GMnum : 99999u);
		BaseGameItemInfo baseGameItemInfo = inventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
		if (baseGameItemInfo != null)
		{
			baseGameItemInfo.ItemCount = Math.Min(baseGameItemInfo.ItemCount + reward.Count, val);
			return baseGameItemInfo;
		}
		BaseGameItemInfo baseGameItemInfo2 = new BaseGameItemInfo
		{
			TemplateId = templateId,
			UniqueId = inventoryData.NextUniqueUid++,
			ItemType = ItemTypeEnum.TYPE_USEABLE,
			ItemCount = Math.Min(reward.Count, val)
		};
		inventoryData.Items[baseGameItemInfo2.UniqueId] = baseGameItemInfo2;
		return baseGameItemInfo2;
	}

	private static uint ReadUInt(JToken? token)
	{
		if (token == null)
		{
			return 0u;
		}
		JTokenType type = token.Type;
		if (1 == 0)
		{
		}
		uint result2;
		switch (type)
		{
		case JTokenType.Integer:
			result2 = token.Value<uint>();
			break;
		case JTokenType.Float:
			result2 = (uint)Math.Max(0m, token.Value<decimal>());
			break;
		case JTokenType.String:
		{
			if (!uint.TryParse(token.Value<string>(), out var result))
			{
				goto default;
			}
			result2 = result;
			break;
		}
		default:
			result2 = 0u;
			break;
		}
		if (1 == 0)
		{
		}
		return result2;
	}
}
