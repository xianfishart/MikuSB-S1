using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Gacha;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

[CallGSApi("Gacha_Launch")]
public class Gacha_Launch : ICallGSHandler
{
	private const uint GachaGid = 5u;

	private const uint GachaSgid = 42u;

	private const uint SidTotalTime = 1u;

	private const uint SidDailyTotalTime = 2u;

	private const uint Interval = 10u;

	private const uint SidTimeInheritStart = 20000u;

	private const uint SidTimeNotInheritStart = 10u;

	private const uint SidAddTimeItem = 1u;

	private const uint SidAddTimeProb = 2u;

	private const uint SidAddProtectType = 3u;

	private const uint SidAddTotalTime = 7u;

	private const int UpSelectIndex = 0;

	private const int UpSelectGetFlagIndex = 1;

	private static readonly Random Rng = new Random();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		GachaLaunchParam req = System.Text.Json.JsonSerializer.Deserialize<GachaLaunchParam>(param);
		bool flag = req == null || req.NId == 0;
		bool flag2 = flag;
		if (!flag2)
		{
			int nTime = req.NTime;
			bool flag3 = ((nTime == 1 || nTime == 10) ? true : false);
			flag2 = !flag3;
		}
		if (flag2)
		{
			await CallGSRouter.SendScript(connection, "Gacha_Launch", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (!GameData.GachaData.TryGetValue((uint)req.NId, out GachaExcel gachaCfg))
		{
			await CallGSRouter.SendScript(connection, "Gacha_Launch", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		List<string> poolNames = (gachaCfg.Pool ?? new List<string>()).Where(GameData.GachaPoolData.ContainsKey).ToList();
		List<GachaPoolItem> allPoolItems = poolNames.SelectMany((string key) => GameData.GachaPoolData[key]).ToList();
		if (allPoolItems.Count == 0 || !GameData.GachaProbabilityData.TryGetValue(gachaCfg.Probability, out GachaProbabilityExcel baseProbCfg))
		{
			await CallGSRouter.SendScript(connection, "Gacha_Launch", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		GachaPityState pityState = LoadPityState(player, gachaCfg);
		GachaUpSelectState upSelectState = LoadUpSelectState(player, gachaCfg);
		GachaRuntimeConfig config = BuildRuntimeConfig(gachaCfg, poolNames);
		List<List<uint>> awards = new List<List<uint>>();
		List<int> tbNew = new List<int>();
		List<bool> tbTrigger = new List<bool>();
		List<Item> syncItems = new List<Item>();
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<List<uint>> costs = ((req.NTime == 10) ? gachaCfg.CastTen : gachaCfg.CastOne);
		List<(BaseGameItemInfo Item, uint Amount)> consumed = new List<(BaseGameItemInfo, uint)>();
		foreach (List<uint> cost in costs ?? new List<List<uint>>())
		{
			if (cost.Count >= 5)
			{
				BaseGameItemInfo costItem = player.InventoryManager.GetNormalItemGDPL((ItemTypeEnum)cost[0], cost[1], cost[2], cost[3]);
				if (costItem == null || costItem.ItemCount < cost[4])
				{
					await CallGSRouter.SendScript(connection, "Gacha_Launch", "{\"sErr\":\"tip.cash_not_enough\"}");
					return;
				}
				consumed.Add((costItem, cost[4]));
			}
		}
		foreach (var (item, amount) in consumed)
		{
			item.ItemCount -= amount;
			sync.Items.Add(item.ToProto());
		}
		for (int i = 0; i < req.NTime; i++)
		{
			bool forceTopUp = config.UpTarget != null && pityState.ProtectType == 2;
			bool hitHardPity = config.ProtectThreshold > 0 && pityState.ItemCount + 1 >= config.ProtectThreshold;
			bool tenDue = pityState.TenCount + 1 >= 10 && !HasGuaranteedTenRarity(config, awards);
			bool useTenGuarantee = (gachaCfg.ProbabilityTen != 0) & tenDue;
			GachaProbabilityExcel probCfg = baseProbCfg;
			if (useTenGuarantee && GameData.GachaProbabilityData.TryGetValue(gachaCfg.ProbabilityTen, out GachaProbabilityExcel tenProbCfg))
			{
				probCfg = tenProbCfg;
			}
			GachaPoolItem item2;
			bool trigger;
			if (hitHardPity)
			{
				item2 = PickGuaranteedItem(gachaCfg, config, forceTopUp, forceType: true);
				trigger = item2 != null;
			}
			else
			{
				int rarity = RollRarity(probCfg);
				item2 = ((forceTopUp && config.UpTarget != null && rarity >= config.TopRarity) ? PickGuaranteedItem(gachaCfg, config, preferUp: true, forceType: true) : PickItem(allPoolItems, rarity));
				trigger = forceTopUp && item2 != null && config.UpTarget != null && item2.Rarity == config.UpTarget.Rarity;
				if (useTenGuarantee && (item2 == null || item2.Rarity < config.TenGuaranteeRarity || (config.TopGenre != 0 && item2.GDPL.Count > 0 && item2.GDPL[0] != config.TopGenre)))
				{
					item2 = PickTenGuaranteeItem(config);
				}
			}
			if (item2 != null && upSelectState.SelectedItem != null && item2.Rarity >= config.TopRarity && (upSelectState.GuaranteedNext || Rng.Next(100) < 50))
			{
				GachaPoolItem selectedItem = FindExactItem(allPoolItems, upSelectState.SelectedItem);
				if (selectedItem != null && selectedItem.Rarity >= config.TopRarity)
				{
					item2 = selectedItem;
				}
			}
			if (item2 == null || item2.GDPL.Count < 4)
			{
				tbTrigger.Add(item: false);
				continue;
			}
			if (config.UpTarget != null && item2.Rarity >= config.TopRarity && (config.TopGenre == 0 || item2.GDPL[0] == config.TopGenre) && (pityState.ProtectType == 2 || Rng.Next(100) < 50))
			{
				GachaPoolItem featured = PickItemFromPool(config.UpTarget.PoolName, config.UpTarget.Rarity, config.TopGenre, forceType: true);
				if (featured != null)
				{
					item2 = featured;
				}
			}
			uint g = item2.GDPL[0];
			uint d = item2.GDPL[1];
			uint p = item2.GDPL[2];
			uint l = item2.GDPL[3];
			awards.Add(new List<uint>(4) { g, d, p, l });
			tbTrigger.Add(trigger);
			UpdatePityState(pityState, config, item2, tenDue);
			UpdateUpSelectState(upSelectState, config, item2);
			ItemTypeEnum itemType = (ItemTypeEnum)g;
			switch (itemType)
			{
			case ItemTypeEnum.TYPE_CARD:
			{
				if (player.CharacterManager.GetCharacterGDPL(itemType, (int)d, (int)p) == null)
				{
					CharacterInfo charInfo = await player.CharacterManager.AddCharacter(itemType, d, p, 1u, 1u, sendPacket: false);
					if (charInfo != null)
					{
						syncItems.Add(charInfo.ToProto());
						tbNew.Add(awards.Count);
					}
					break;
				}
				CardExcel cardCfg = GameData.CardData.Values.FirstOrDefault((CardExcel x) => (ulong)x.Genre == (ulong)itemType && x.Detail == d && x.Particular == p);
				List<List<int>> conversions = ((cardCfg != null && cardCfg.SameConvert.Count > 0) ? cardCfg.SameConvert : (cardCfg?.Pieces ?? new List<List<int>>()));
				foreach (List<int> conversion in conversions)
				{
					if (conversion.Count >= 5 && conversion[4] > 0)
					{
						List<uint> reward = (from x in conversion.Take(5)
							select (uint)Math.Max(0, x)).ToList();
						await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
					}
				}
				break;
			}
			case ItemTypeEnum.TYPE_WEAPON:
			{
				GameWeaponInfo weaponInfo = await player.InventoryManager.AddWeaponItem(itemType, d, p, l, 1u, sendPacket: false);
				if (weaponInfo != null)
				{
					syncItems.Add(weaponInfo.ToProto());
				}
				int rarity2 = item2.Rarity;
				if (1 == 0)
				{
				}
				List<uint> list = rarity2 switch
				{
					3 => new List<uint> { 5u, 13u, 1u, 3u, 25u }, 
					4 => new List<uint> { 5u, 13u, 1u, 4u, 5u }, 
					5 => new List<uint> { 5u, 13u, 1u, 4u, 25u }, 
					_ => null, 
				};
				if (1 == 0)
				{
				}
				List<uint> codeReward = list;
				if (codeReward != null)
				{
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, codeReward);
				}
				break;
			}
			case ItemTypeEnum.TYPE_SUPPORT:
			{
				GameSupportCardInfo cardInfo = await player.InventoryManager.AddSupportCardItem(d, p, l, 1u, sendPacket: false);
				if (cardInfo != null)
				{
					syncItems.Add(cardInfo.ToProto());
				}
				break;
			}
			}
			tenProbCfg = null;
		}
		if (awards.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "Gacha_Launch", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		SavePityState(player, gachaCfg, pityState, awards.Count, sync);
		SaveUpSelectState(player, gachaCfg, upSelectState, sync);
		foreach (List<uint> item3 in awards)
		{
			GachaRecordStore.Append(gdpl: item3, uid: player.Uid, key: $"GACHA_{req.NId}");
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		sync.Items.AddRange(syncItems);
		string rsp = BuildResponse(req.NId, awards, tbNew, tbTrigger);
		await CallGSRouter.SendScript(connection, "Gacha_Launch", rsp, sync);
	}

	private static bool HasGuaranteedTenRarity(GachaRuntimeConfig config, List<List<uint>> awards)
	{
		if (awards.Count == 0)
		{
			return false;
		}
		int num = ((awards.Count >= 9) ? (awards.Count - 9) : 0);
		for (int i = num; i < awards.Count; i++)
		{
			List<uint> list = awards[i];
			if (list.Count >= 4)
			{
				GachaPoolItem gachaPoolItem = FindPoolItemByGdpl(config.AllPoolItems, list);
				if (gachaPoolItem != null && gachaPoolItem.Rarity >= config.TenGuaranteeRarity && (config.TopGenre == 0 || gachaPoolItem.GDPL[0] == config.TopGenre))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static GachaPoolItem? FindPoolItemByGdpl(List<GachaPoolItem> pool, List<uint> gdpl)
	{
		return pool.FirstOrDefault((GachaPoolItem x) => x.GDPL.Count >= 4 && x.GDPL[0] == gdpl[0] && x.GDPL[1] == gdpl[1] && x.GDPL[2] == gdpl[2] && x.GDPL[3] == gdpl[3]);
	}

	private static GachaPoolItem? FindExactItem(List<GachaPoolItem> pool, uint[] gdpl)
	{
		return pool.FirstOrDefault((GachaPoolItem x) => x.GDPL.Count >= 4 && x.GDPL[0] == gdpl[0] && x.GDPL[1] == gdpl[1] && x.GDPL[2] == gdpl[2] && x.GDPL[3] == gdpl[3]);
	}

	private static GachaRuntimeConfig BuildRuntimeConfig(GachaExcel gachaCfg, List<string> poolNames)
	{
		List<GachaPoolItem> list = poolNames.SelectMany((string name) => GameData.GachaPoolData[name]).ToList();
		List<PoolRarityRef> list2 = ParsePoolRarities(gachaCfg.ProtectNum);
		PoolRarityRef poolRarityRef = ParseSinglePoolRarity(gachaCfg.UpNum);
		int num = new int[1] { poolRarityRef?.Rarity ?? 0 }.Concat(list2.Select((PoolRarityRef x) => x.Rarity)).Max();
		if (num <= 0)
		{
			num = ((list.Count != 0) ? list.Max((GachaPoolItem x) => x.Rarity) : 0);
		}
		return new GachaRuntimeConfig
		{
			AllPoolItems = list,
			ProtectThreshold = ParseThreshold(gachaCfg.ProtectNum),
			ProtectPools = list2,
			UpTarget = poolRarityRef,
			TopRarity = num,
			TopGenre = (gachaCfg.Tag?.FirstOrDefault() ?? 0),
			TenGuaranteeRarity = 4
		};
	}

	private static int ParseThreshold(JToken? token)
	{
		if (!(token is JArray { Count: not 0 } jArray))
		{
			return 0;
		}
		JToken jToken = jArray[0];
		return (jToken != null) ? jToken.Value<int>() : 0;
	}

	private static List<PoolRarityRef> ParsePoolRarities(JToken? token)
	{
		List<PoolRarityRef> list = new List<PoolRarityRef>();
		if (!(token is JArray { Count: >=2 } jArray) || !(jArray[1] is JArray source))
		{
			return list;
		}
		foreach (JArray item in source.OfType<JArray>())
		{
			if (item.Count >= 2)
			{
				JToken jToken = item[0];
				string text = ((jToken != null) ? jToken.Value<string>() : null);
				JToken jToken2 = item[1];
				int num = ((jToken2 != null) ? jToken2.Value<int>() : 0);
				if (!string.IsNullOrWhiteSpace(text) && num > 0)
				{
					list.Add(new PoolRarityRef(text, num));
				}
			}
		}
		return list;
	}

	private static PoolRarityRef? ParseSinglePoolRarity(JToken? token)
	{
		if (!(token is JArray { Count: >=2 } jArray) || !(jArray[1] is JArray { Count: >=2 } jArray2))
		{
			return null;
		}
		JToken jToken = jArray2[0];
		string text = ((jToken != null) ? jToken.Value<string>() : null);
		JToken jToken2 = jArray2[1];
		int num = ((jToken2 != null) ? jToken2.Value<int>() : 0);
		return (string.IsNullOrWhiteSpace(text) || num <= 0) ? null : new PoolRarityRef(text, num);
	}

	private static GachaPityState LoadPityState(PlayerInstance player, GachaExcel gachaCfg)
	{
		uint baseSid = GetBaseSid(gachaCfg);
		return new GachaPityState
		{
			ItemCount = (int)GetAttr(player, 5u, baseSid + 1),
			TenCount = (int)GetAttr(player, 5u, baseSid + 2),
			ProtectType = Math.Max(1, (int)GetAttr(player, 5u, baseSid + 3)),
			PoolTotalTime = (int)GetAttr(player, 5u, baseSid + 7)
		};
	}

	private static void SavePityState(PlayerInstance player, GachaExcel gachaCfg, GachaPityState state, int drawCount, NtfSyncPlayer sync)
	{
		uint baseSid = GetBaseSid(gachaCfg);
		SetAttr(player, sync, 5u, 1u, GetAttr(player, 5u, 1u) + (uint)drawCount);
		SetAttr(player, sync, 5u, 2u, GetAttr(player, 5u, 2u) + (uint)drawCount);
		SetAttr(player, sync, 5u, baseSid + 1, (uint)state.ItemCount);
		SetAttr(player, sync, 5u, baseSid + 2, (uint)state.TenCount);
		SetAttr(player, sync, 5u, baseSid + 3, (uint)Math.Max(1, state.ProtectType));
		SetAttr(player, sync, 5u, baseSid + 7, (uint)(state.PoolTotalTime + drawCount));
	}

	private static GachaUpSelectState LoadUpSelectState(PlayerInstance player, GachaExcel gachaCfg)
	{
		if (gachaCfg.UpSelect != 1)
		{
			return new GachaUpSelectState();
		}
		string text = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 42 && x.Sid == gachaCfg.ID)?.Val;
		if (string.IsNullOrWhiteSpace(text))
		{
			return new GachaUpSelectState();
		}
		try
		{
			JArray jArray = JArray.Parse(text);
			uint[] selectedItem = null;
			if (jArray.Count > 0 && jArray[0] is JArray { Count: >=4 } jArray2)
			{
				uint[] array = new uint[4];
				JToken jToken = jArray2[0];
				array[0] = ((jToken != null) ? jToken.Value<uint>() : 0u);
				JToken jToken2 = jArray2[1];
				array[1] = ((jToken2 != null) ? jToken2.Value<uint>() : 0u);
				JToken jToken3 = jArray2[2];
				array[2] = ((jToken3 != null) ? jToken3.Value<uint>() : 0u);
				JToken jToken4 = jArray2[3];
				array[3] = ((jToken4 != null) ? jToken4.Value<uint>() : 0u);
				selectedItem = array;
			}
			GachaUpSelectState gachaUpSelectState = new GachaUpSelectState
			{
				SelectedItem = selectedItem
			};
			int guaranteedNext;
			if (jArray.Count > 1)
			{
				JToken jToken5 = jArray[1];
				guaranteedNext = ((((jToken5 != null) ? jToken5.Value<int>() : 0) == 1) ? 1 : 0);
			}
			else
			{
				guaranteedNext = 0;
			}
			gachaUpSelectState.GuaranteedNext = (byte)guaranteedNext != 0;
			gachaUpSelectState.RawState = jArray;
			return gachaUpSelectState;
		}
		catch
		{
			return new GachaUpSelectState();
		}
	}

	private static void SaveUpSelectState(PlayerInstance player, GachaExcel gachaCfg, GachaUpSelectState state, NtfSyncPlayer sync)
	{
		if (gachaCfg.UpSelect == 1 && state.RawState != null)
		{
			EnsureArraySize(state.RawState, 2);
			state.RawState[1] = (state.GuaranteedNext ? 1 : 0);
			string value = state.RawState.ToString(Formatting.None);
			player.SetStrAttr(42u, gachaCfg.ID, value);
			sync.CustomStr[player.ToShiftedAttrKey(42u, gachaCfg.ID)] = value;
		}
	}

	private static uint GetBaseSid(GachaExcel gachaCfg)
	{
		if (gachaCfg.ProtectTag.HasValue)
		{
			return 20000 + gachaCfg.ProtectTag.Value * 10;
		}
		return 10 + gachaCfg.ID * 10;
	}

	private static uint GetAttr(PlayerInstance player, uint gid, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid)?.Val ?? 0;
	}

	private static void SetAttr(PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val = value;
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}

	private static void UpdatePityState(GachaPityState state, GachaRuntimeConfig config, GachaPoolItem item, bool tenDue)
	{
		bool flag = config.TopGenre == 0 || (item.GDPL.Count > 0 && item.GDPL[0] == config.TopGenre);
		if (flag && item.Rarity >= config.TenGuaranteeRarity)
		{
			state.TenCount = ((tenDue && item.Rarity > config.TenGuaranteeRarity) ? 9 : 0);
		}
		else
		{
			state.TenCount++;
		}
		if (flag && item.Rarity >= config.TopRarity)
		{
			state.ItemCount = 0;
			if (config.UpTarget != null)
			{
				state.ProtectType = (IsFromPool(item, config.UpTarget) ? 1 : 2);
			}
			else
			{
				state.ProtectType = 1;
			}
		}
		else
		{
			state.ItemCount++;
		}
	}

	private static void UpdateUpSelectState(GachaUpSelectState state, GachaRuntimeConfig config, GachaPoolItem item)
	{
		if (state.SelectedItem != null && item.Rarity >= config.TopRarity && (config.TopGenre == 0 || (item.GDPL.Count != 0 && item.GDPL[0] == config.TopGenre)))
		{
			state.GuaranteedNext = !MatchesGdpl(item, state.SelectedItem);
		}
	}

	private static bool MatchesGdpl(GachaPoolItem item, uint[] gdpl)
	{
		return item.GDPL.Count >= 4 && item.GDPL[0] == gdpl[0] && item.GDPL[1] == gdpl[1] && item.GDPL[2] == gdpl[2] && item.GDPL[3] == gdpl[3];
	}

	private static void EnsureArraySize(JArray state, int size)
	{
		while (state.Count < size)
		{
			state.Add(JValue.CreateNull());
		}
	}

	private static bool IsFromPool(GachaPoolItem item, PoolRarityRef target)
	{
		List<GachaPoolItem> value;
		return item.Rarity == target.Rarity && GameData.GachaPoolData.TryGetValue(target.PoolName, out value) && value.Any((GachaPoolItem x) => x.ID == item.ID);
	}

	private static int RollRarity(GachaProbabilityExcel prob)
	{
		int[] weights = prob.Weights;
		int maxValue = weights.Sum();
		int num = Rng.Next(maxValue);
		int num2 = 0;
		for (int i = 0; i < weights.Length; i++)
		{
			num2 += weights[i];
			if (num < num2)
			{
				return i + 1;
			}
		}
		return 3;
	}

	private static GachaPoolItem? PickTenGuaranteeItem(GachaRuntimeConfig config)
	{
		List<GachaPoolItem> list = config.AllPoolItems.Where((GachaPoolItem x) => x.Rarity >= config.TenGuaranteeRarity && (config.TopGenre == 0 || (x.GDPL.Count > 0 && x.GDPL[0] == config.TopGenre))).ToList();
		if (list.Count == 0)
		{
			return null;
		}
		int rarity = list.Min((GachaPoolItem x) => x.Rarity);
		return PickItem(list, rarity);
	}

	private static GachaPoolItem? PickGuaranteedItem(GachaExcel gachaCfg, GachaRuntimeConfig config, bool preferUp, bool forceType = false)
	{
		if (preferUp && config.UpTarget != null)
		{
			GachaPoolItem gachaPoolItem = PickItemFromPool(config.UpTarget.PoolName, config.UpTarget.Rarity, config.TopGenre, forceType);
			if (gachaPoolItem != null)
			{
				return gachaPoolItem;
			}
		}
		foreach (PoolRarityRef protectPool in config.ProtectPools)
		{
			GachaPoolItem gachaPoolItem2 = PickItemFromPool(protectPool.PoolName, protectPool.Rarity, config.TopGenre, forceType);
			if (gachaPoolItem2 != null)
			{
				return gachaPoolItem2;
			}
		}
		List<GachaPoolItem> list = config.AllPoolItems.Where((GachaPoolItem x) => (!forceType || config.TopGenre == 0 || x.GDPL[0] == config.TopGenre) && x.Rarity >= config.TenGuaranteeRarity).ToList();
		return (list.Count > 0) ? PickItem(list, list.Max((GachaPoolItem x) => x.Rarity)) : null;
	}

	private static GachaPoolItem? PickItemFromPool(string poolName, int rarity, int genre = 0, bool forceType = false)
	{
		if (!GameData.GachaPoolData.TryGetValue(poolName, out List<GachaPoolItem> value))
		{
			return null;
		}
		if (forceType && genre != 0)
		{
			value = value.Where((GachaPoolItem x) => x.GDPL.Count > 0 && x.GDPL[0] == genre).ToList();
		}
		return PickItem(value, rarity);
	}

	private static GachaPoolItem? PickItem(List<GachaPoolItem> pool, int rarity)
	{
		List<GachaPoolItem> list = pool.Where((GachaPoolItem x) => x.Rarity == rarity).ToList();
		if (list.Count == 0)
		{
			list = pool.Where((GachaPoolItem x) => x.Rarity == rarity - 1).ToList();
			if (list.Count == 0)
			{
				return pool.FirstOrDefault();
			}
		}
		int num = list.Sum((GachaPoolItem x) => x.Weight);
		if (num <= 0)
		{
			return list[Rng.Next(list.Count)];
		}
		int num2 = Rng.Next(num);
		int num3 = 0;
		foreach (GachaPoolItem item in list)
		{
			num3 += item.Weight;
			if (num2 < num3)
			{
				return item;
			}
		}
		return list.Last();
	}

	private static string BuildResponse(int nId, List<List<uint>> awards, List<int> tbNew, List<bool> tbTrigger)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("{\"nId\":");
		stringBuilder.Append(nId);
		stringBuilder.Append(",\"tbAwards\":[");
		for (int i = 0; i < awards.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(',');
			}
			stringBuilder.Append('[');
			stringBuilder.Append(string.Join(',', awards[i]));
			stringBuilder.Append(']');
		}
		stringBuilder.Append("],\"nBoxCount\":0,\"tbNew\":[");
		stringBuilder.Append(string.Join(',', tbNew));
		stringBuilder.Append("],\"tbTrigger\":[");
		stringBuilder.Append(string.Join(',', tbTrigger.Select((bool b) => b ? "true" : "false")));
		stringBuilder.Append("]}");
		return stringBuilder.ToString();
	}
}
