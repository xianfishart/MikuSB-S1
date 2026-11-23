using System;
using System.Collections.Generic;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

internal static class DonoDuelBag
{
	private static readonly Logger Logger = new Logger("DonoDuelBag");

	private const uint GroupId = 185u;

	private const uint UniqueIdSid = 2u;

	private const uint StockroomSid = 3u;

	private const uint BlockStartSid = 1001u;

	private const uint BlockEndSid = 4500u;

	private const uint BlockStep = 10u;

	private const uint MaxUniqueId = 350u;

	private const int BlockSlots = 7;

	private const uint DefaultStar = 1u;

	private const uint DefaultQuality = 1u;

	private const uint MaxStar = 5u;

	private const uint StarterMissionId = 101u;

	public static List<(uint TemplateId, uint Star, uint Quality)> ReadCardRows(JToken? reward3)
	{
		List<(uint, uint, uint)> list = new List<(uint, uint, uint)>();
		if (!(reward3 is JArray jArray))
		{
			return list;
		}
		foreach (JToken item in jArray)
		{
			if (item is JArray { Count: >=1 } jArray2)
			{
				uint num = ReadUint(jArray2[0]);
				if (num != 0)
				{
					uint num2 = ((jArray2.Count > 1) ? ReadUint(jArray2[1]) : 0u);
					uint num3 = ((jArray2.Count > 2) ? ReadUint(jArray2[2]) : 0u);
					list.Add((num, (num2 == 0) ? 1u : num2, (num3 == 0) ? 1u : num3));
				}
			}
		}
		return list;
	}

	public static List<uint> GrantCards(PlayerInstance player, IReadOnlyList<(uint TemplateId, uint Star, uint Quality)> cards, NtfSyncPlayer? sync)
	{
		List<uint> list = new List<uint>();
		if (cards.Count == 0)
		{
			return list;
		}
		// 第 488 节：卡位改成「优先复用被回收的空槽」。
		// 客户端把 350 这个上限硬编码进了字节码（DonoDuel.Max_BagItem_Unique 只是展示用，
		// 改它不影响 GetBagAllCard/GetBagItemAndConfig 的边界判断），所以 uid 一旦越过 350，
		// 客户端就完全读不到这些卡。而 185/2 是「已分配到的最大 uid」，只增不减：
		// 玩家回收了几百张之后 uid 依然在 350，新卡再也发不出去。
		// 这里改成先扫 1..MaxUniqueId 的空槽，没有空槽才往后追加。
		uint nextUid = Math.Min(player.Attributes.GetValue(185u, 2u), MaxUniqueId);
		uint stock = player.Attributes.GetValue(185u, 3u);
		List<uint> free = new List<uint>();
		for (uint probe = 1u; probe <= MaxUniqueId && free.Count < cards.Count; probe++)
		{
			if (!HasCard(player, probe))
			{
				free.Add(probe);
			}
		}
		int freeIndex = 0;
		foreach (var card in cards)
		{
			uint uid;
			if (freeIndex < free.Count)
			{
				uid = free[freeIndex++];
			}
			else
			{
				if (nextUid >= MaxUniqueId)
				{
					Logger.Warn($"Donoduel bag is full ({stock}/{MaxUniqueId}) for uid={player.Uid}; " + "remaining card(s) dropped.");
					break;
				}
				nextUid++;
				uid = nextUid;
			}
			uint blockSid = BlockStartSid + (uid - 1) * BlockStep;
			if (blockSid + BlockSlots - 1 > BlockEndSid)
			{
				Logger.Warn($"Donoduel card block {uid} would end past sid {BlockEndSid}; stopping.");
				break;
			}
			uint star = Math.Min(card.Star, MaxStar);
			Set(player, blockSid, (card.TemplateId << 16) | (star & 0xFFFF), sync);
			Set(player, blockSid + 1, card.Quality << 16, sync);
			DonoDuelHeroCardExcel valueOrDefault = GameData.DonoDuelHeroCardData.GetValueOrDefault(card.TemplateId);
			Set(player, blockSid + 2, valueOrDefault?.AttackInitial ?? 0, sync);
			Set(player, blockSid + 3, valueOrDefault?.DefenseInitial ?? 0, sync);
			Set(player, blockSid + 4, valueOrDefault?.HealthInitial ?? 0, sync);
			Set(player, blockSid + 5, 0u, sync);
			Set(player, blockSid + 6, 0u, sync);
			list.Add(uid);
			stock++;
		}
		if (list.Count == 0)
		{
			return list;
		}
		Set(player, 2u, nextUid, sync);
		Set(player, 3u, stock, sync);
		return list;
	}

	public static bool HasCard(PlayerInstance player, uint uniqueId)
	{
		if (uniqueId == 0 || uniqueId  > MaxUniqueId)
		{
			return false;
		}
		uint sid = 1001 + (uniqueId - 1) * 10;
		return player.Attributes.GetValue(185u, sid) >> 16 != 0;
	}

	public static uint GetTemplateId(PlayerInstance player, uint uniqueId)
	{
		if (uniqueId == 0 || uniqueId  > MaxUniqueId)
		{
			return 0u;
		}
		return player.Attributes.GetValue(185u, 1001 + (uniqueId - 1) * 10) >> 16;
	}

	public static bool HasTemplate(PlayerInstance player, uint templateId)
	{
		if (templateId == 0)
		{
			return false;
		}
		uint num = Math.Min(player.Attributes.GetValue(185u, 2u), MaxUniqueId);
		for (uint num2 = 1u; num2 <= num; num2++)
		{
			if (GetTemplateId(player, num2) == templateId)
			{
				return true;
			}
		}
		return false;
	}

	public static uint CountTemplateStar(PlayerInstance player, uint templateId, uint star)
	{
		if (templateId == 0)
		{
			return 0u;
		}
		uint num = 0u;
		uint num2 = Math.Min(player.Attributes.GetValue(185u, 2u), MaxUniqueId);
		for (uint num3 = 1u; num3 <= num2; num3++)
		{
			uint sid = 1001 + (num3 - 1) * 10;
			uint value = player.Attributes.GetValue(185u, sid);
			if (value >> 16 == templateId && (value & 0xFFFF) == star)
			{
				num++;
			}
		}
		return num;
	}

	public static uint GetFlags(PlayerInstance player, uint uniqueId)
	{
		if (uniqueId == 0 || uniqueId  > MaxUniqueId)
		{
			return 0u;
		}
		uint num = 1001 + (uniqueId - 1) * 10;
		return player.Attributes.GetValue(185u, num + 1) & 0xFFFF;
	}

	public static void SetFlags(PlayerInstance player, uint uniqueId, uint flags, NtfSyncPlayer? sync)
	{
		if (uniqueId != 0 && uniqueId <= MaxUniqueId)
		{
			uint num = 1001 + (uniqueId - 1) * 10;
			uint num2 = player.Attributes.GetValue(185u, num + 1) >> 16;
			Set(player, num + 1, (num2 << 16) | (flags & 0xFFFF), sync);
		}
	}

	public static void SetSlot(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		Set(player, sid, value, sync);
	}

	public static void Save(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
	}

	public static bool EnsureStarterDeck(PlayerInstance player, NtfSyncPlayer? sync = null)
	{
		if (player.Attributes.GetValue(185u, 2u) != 0)
		{
			return false;
		}
		if (!GameData.DonoDuelStoryTaskData.TryGetValue(101u, out DonoDuelStoryTaskExcel value))
		{
			return false;
		}
		if (!DonoDuelTaskState.IsReceived(player, 101u))
		{
			return false;
		}
		List<(uint, uint, uint)> cards = ReadCardRows(value.Reward3);
		List<uint> list = GrantCards(player, cards, sync);
		if (list.Count == 0)
		{
			return false;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		Logger.Info($"Seeded the Donoduel starter deck ({list.Count} card(s)) for uid={player.Uid}.");
		return true;
	}

	public static uint BlockOf(uint uniqueId)
	{
		return 1001 + (uniqueId - 1) * 10;
	}

	public static uint GetStar(PlayerInstance player, uint uniqueId)
	{
		if (uniqueId == 0 || uniqueId  > MaxUniqueId)
		{
			return 0u;
		}
		return player.Attributes.GetValue(185u, BlockOf(uniqueId)) & 0xFFFF;
	}

	public static uint GetQuality(PlayerInstance player, uint uniqueId)
	{
		if (uniqueId == 0 || uniqueId  > MaxUniqueId)
		{
			return 0u;
		}
		return player.Attributes.GetValue(185u, BlockOf(uniqueId) + 1) >> 16;
	}

	public static uint GetAttr(PlayerInstance player, uint uniqueId, uint position)
	{
		bool flag = uniqueId == 0 || uniqueId > MaxUniqueId;
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((position < 1 || position > 3) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			return 0u;
		}
		return player.Attributes.GetValue(185u, BlockOf(uniqueId) + 1 + position);
	}

	public static void SetAttr(PlayerInstance player, uint uniqueId, uint position, uint value, NtfSyncPlayer? sync)
	{
		bool flag = uniqueId == 0 || uniqueId > MaxUniqueId;
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((position < 1 || position > 3) ? true : false);
			flag2 = flag3;
		}
		if (!flag2)
		{
			Set(player, BlockOf(uniqueId) + 1 + position, value, sync);
		}
	}

	public static void SetStar(PlayerInstance player, uint uniqueId, uint star, NtfSyncPlayer? sync)
	{
		if (uniqueId != 0 && uniqueId <= MaxUniqueId)
		{
			uint sid = BlockOf(uniqueId);
			uint num = player.Attributes.GetValue(185u, sid) >> 16;
			Set(player, sid, (num << 16) | (star & 0xFFFF), sync);
		}
	}

	public static void SetQuality(PlayerInstance player, uint uniqueId, uint quality, NtfSyncPlayer? sync)
	{
		if (uniqueId != 0 && uniqueId <= MaxUniqueId)
		{
			uint num = BlockOf(uniqueId);
			uint num2 = player.Attributes.GetValue(185u, num + 1) & 0xFFFF;
			Set(player, num + 1, (quality << 16) | num2, sync);
		}
	}

	public static uint GetSkill(PlayerInstance player, uint uniqueId, int slot)
	{
		bool flag = uniqueId == 0 || uniqueId > MaxUniqueId;
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((slot < 1 || slot > 3) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			return 0u;
		}
		uint num = BlockOf(uniqueId);
		uint value = player.Attributes.GetValue(185u, num + 5);
		uint value2 = player.Attributes.GetValue(185u, num + 6);
		if (1 == 0)
		{
		}
		uint result = slot switch
		{
			1 => value >> 16, 
			2 => value & 0xFFFF, 
			_ => value2 >> 16, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public static void SetSkills(PlayerInstance player, uint uniqueId, uint skill1, uint skill2, uint skill3, NtfSyncPlayer? sync)
	{
		if (uniqueId != 0 && uniqueId <= MaxUniqueId)
		{
			uint num = BlockOf(uniqueId);
			Set(player, num + 5, (skill1 << 16) | (skill2 & 0xFFFF), sync);
			Set(player, num + 6, skill3 << 16, sync);
		}
	}

	public static bool IsLocked(PlayerInstance player, uint uniqueId)
	{
		return (GetFlags(player, uniqueId) & 1) != 0;
	}

	public static bool IsInAnyFormation(PlayerInstance player, uint uniqueId)
	{
		if (uniqueId == 0)
		{
			return false;
		}
		for (uint num = 10u; num <= 23; num++)
		{
			uint value = player.Attributes.GetValue(185u, num);
			if (value >> 16 == uniqueId || (value & 0xFFFF) == uniqueId)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 把某张卡从所有编队里摘掉（第 464 节：出阵卡可作为升星材料，消耗后自动下阵）。
	/// </summary>
	public static bool RemoveFromFormation(PlayerInstance player, uint uniqueId, NtfSyncPlayer? sync)
	{
		if (uniqueId == 0)
		{
			return false;
		}
		bool changed = false;
		for (uint sid = 10u; sid <= 23u; sid++)
		{
			uint value = player.Attributes.GetValue(185u, sid);
			if (value == 0)
			{
				continue;
			}
			uint high = value >> 16;
			uint low = value & 0xFFFF;
			if (high != uniqueId && low != uniqueId)
			{
				continue;
			}
			uint newHigh = ((high == uniqueId) ? 0u : high);
			uint newLow = ((low == uniqueId) ? 0u : low);
			Set(player, sid, (newHigh << 16) | newLow, sync);
			changed = true;
		}
		return changed;
	}

	public static uint GetStockroom(PlayerInstance player)
	{
		return player.Attributes.GetValue(185u, 3u);
	}

	public static void SetStockroom(PlayerInstance player, uint value, NtfSyncPlayer? sync)
	{
		Set(player, 3u, value, sync);
	}

	public static void DestroyCard(PlayerInstance player, uint uniqueId, NtfSyncPlayer? sync)
	{
		if (uniqueId != 0 && uniqueId <= MaxUniqueId)
		{
			uint num = BlockOf(uniqueId);
			for (int i = 0; i < 7; i++)
			{
				Set(player, num + (uint)i, 0u, sync);
			}
			uint stockroom = GetStockroom(player);
			if (stockroom != 0)
			{
				SetStockroom(player, stockroom - 1, sync);
			}
		}
	}

	private static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
	{
		PlayerAttr attr = player.Attributes.Set(185u, sid, value);
		if (sync != null)
		{
			player.Attributes.SyncTo(sync, attr);
		}
	}

	private static uint ReadUint(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null)
		{
			return 0u;
		}
		try
		{
			JTokenType type = token.Type;
			if (1 == 0)
			{
			}
			uint result = type switch
			{
				JTokenType.Integer => token.Value<uint>(), 
				JTokenType.Float => (uint)token.Value<double>(), 
				_ => uint.TryParse(token.ToString().Trim().Trim(new char[3] { '[', ']', '"' }), out var result2) ? result2 : 0u, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
		catch
		{
			return 0u;
		}
	}
}
