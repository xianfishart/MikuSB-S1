using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Inventory;

internal sealed class RecycleConfig
{
	private readonly Dictionary<int, RecycleEntry> _entries;

	private readonly List<SupplyTemplate> _weaponSupplies;

	private readonly List<SupplyTemplate> _supportSupplies;

	private readonly Dictionary<int, ulong> _weaponLevelExp;

	private readonly Dictionary<int, ulong> _supportLevelExp;

	private readonly Dictionary<int, ulong> _weaponLevelExpSsr;

	private readonly Dictionary<int, ulong> _supportLevelExpSsr;

	private RecycleConfig(Dictionary<int, RecycleEntry> entries, List<SupplyTemplate> weaponSupplies, List<SupplyTemplate> supportSupplies, Dictionary<int, ulong> weaponLevelExp, Dictionary<int, ulong> weaponLevelExpSsr, Dictionary<int, ulong> supportLevelExp, Dictionary<int, ulong> supportLevelExpSsr)
	{
		_entries = entries;
		_weaponSupplies = weaponSupplies;
		_supportSupplies = supportSupplies;
		_weaponLevelExp = weaponLevelExp;
		_weaponLevelExpSsr = weaponLevelExpSsr;
		_supportLevelExp = supportLevelExp;
		_supportLevelExpSsr = supportLevelExpSsr;
	}

	public static RecycleConfig Load()
	{
		Dictionary<int, RecycleEntry> dictionary = new Dictionary<int, RecycleEntry>();
		foreach (RecycleExcel value in GameData.RecycleData.Values)
		{
			List<IReadOnlyList<uint>> fixedRewards = ParseRewards(value.RecycleReward);
			uint uInt = GetUInt(value.RecycleBase);
			decimal recycleRatio = GetDecimal(value.RecycleRatio);
			dictionary[value.ID] = new RecycleEntry(fixedRewards, uInt, recycleRatio);
		}
		List<SupplyTemplate> list = new List<SupplyTemplate>();
		List<SupplyTemplate> list2 = new List<SupplyTemplate>();
		foreach (SuppliesExcel allSuppliesDatum in GameData.AllSuppliesData)
		{
			if (allSuppliesDatum.ProvideExp != 0)
			{
				if (allSuppliesDatum.Genre == 5 && allSuppliesDatum.Detail == 2)
				{
					list.Add(new SupplyTemplate(allSuppliesDatum.Genre, allSuppliesDatum.Detail, allSuppliesDatum.Particular, allSuppliesDatum.Level, allSuppliesDatum.ProvideExp));
				}
				else if (allSuppliesDatum.Genre == 5 && allSuppliesDatum.Detail == 3)
				{
					list2.Add(new SupplyTemplate(allSuppliesDatum.Genre, allSuppliesDatum.Detail, allSuppliesDatum.Particular, allSuppliesDatum.Level, allSuppliesDatum.ProvideExp));
				}
			}
		}
		list.Sort((SupplyTemplate a, SupplyTemplate b) => b.ProvideExp.CompareTo(a.ProvideExp));
		list2.Sort((SupplyTemplate a, SupplyTemplate b) => b.ProvideExp.CompareTo(a.ProvideExp));
		Dictionary<int, ulong> weaponLevelExp = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select((UpgradeExpExcel x) => (Lv: x.Lv, WeaponNeedExp: x.WeaponNeedExp)));
		Dictionary<int, ulong> weaponLevelExpSsr = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select((UpgradeExpExcel x) => (Lv: x.Lv, SSRWeaponNeedExp: x.SSRWeaponNeedExp)));
		Dictionary<int, ulong> supportLevelExp = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select((UpgradeExpExcel x) => (Lv: x.Lv, SusNeedExp: x.SusNeedExp)));
		Dictionary<int, ulong> supportLevelExpSsr = BuildLevelExpTable(GameData.UpgradeExpData.Values.Select((UpgradeExpExcel x) => (Lv: x.Lv, SSRSusNeedExp: x.SSRSusNeedExp)));
		return new RecycleConfig(dictionary, list, list2, weaponLevelExp, weaponLevelExpSsr, supportLevelExp, supportLevelExpSsr);
	}

	public bool HasConfig(int recycleId)
	{
		return _entries.ContainsKey(recycleId);
	}

	public List<IReadOnlyList<uint>> CalcRewards(BaseGameItemInfo item, int recycleId)
	{
		if (!_entries.TryGetValue(recycleId, out var value))
		{
			return new List<IReadOnlyList<uint>>();
		}
		List<IReadOnlyList<uint>> list = new List<IReadOnlyList<uint>>(value.FixedRewards);
		List<IReadOnlyList<uint>> collection = CalcExpRewards(item, value);
		list.AddRange(collection);
		return list;
	}

	private List<IReadOnlyList<uint>> CalcExpRewards(BaseGameItemInfo item, RecycleEntry entry)
	{
		if (entry.RecycleRatio == 0m)
		{
			return new List<IReadOnlyList<uint>>();
		}
		List<SupplyTemplate> list;
		Dictionary<int, ulong> dictionary;
		if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
		{
			list = _weaponSupplies;
			int itemColor = GetItemColor(item);
			dictionary = ((itemColor == 5) ? _weaponLevelExpSsr : _weaponLevelExp);
		}
		else
		{
			if (item.ItemType != ItemTypeEnum.TYPE_SUPPORT)
			{
				return new List<IReadOnlyList<uint>>();
			}
			list = _supportSupplies;
			int itemColor2 = GetItemColor(item);
			dictionary = ((itemColor2 == 5) ? _supportLevelExpSsr : _supportLevelExp);
		}
		ulong num = entry.RecycleBase;
		ulong valueOrDefault = dictionary.GetValueOrDefault((int)item.Level);
		ulong num2 = (ulong)Math.Floor((double)(num + valueOrDefault + item.Exp) * (double)entry.RecycleRatio);
		if (num2 == 0L || list.Count == 0)
		{
			return new List<IReadOnlyList<uint>>();
		}
		List<IReadOnlyList<uint>> list2 = new List<IReadOnlyList<uint>>();
		ulong num3 = num2;
		foreach (SupplyTemplate item2 in list)
		{
			if (num3 == 0)
			{
				break;
			}
			ulong num4 = num3 / item2.ProvideExp;
			if (num4 != 0)
			{
				num3 -= num4 * item2.ProvideExp;
				list2.Add(new _003C_003Ez__ReadOnlyArray<uint>(new uint[5]
				{
					item2.Genre,
					item2.Detail,
					item2.Particular,
					item2.Level,
					(uint)Math.Min(num4, 99999uL)
				}));
			}
		}
		return list2;
	}

	private static List<IReadOnlyList<uint>> ParseRewards(JToken? token)
	{
		if (token == null)
		{
			return new List<IReadOnlyList<uint>>();
		}
		if (token is JArray jArray)
		{
			List<IReadOnlyList<uint>> list = new List<IReadOnlyList<uint>>();
			foreach (JToken item in jArray)
			{
				if (item is JArray { Count: >=4 } jArray2)
				{
					uint[] array = jArray2.Select((JToken x) => x.Value<uint>()).ToArray();
					if (array.Length < 5)
					{
						uint[] array2 = array;
						int num = 0;
						uint[] array3 = new uint[1 + array2.Length];
						ReadOnlySpan<uint> readOnlySpan = new ReadOnlySpan<uint>(array2);
						readOnlySpan.CopyTo(new Span<uint>(array3).Slice(num, readOnlySpan.Length));
						num += readOnlySpan.Length;
						array3[num] = 1u;
						array = array3;
					}
					list.Add(array);
				}
			}
			return list;
		}
		return new List<IReadOnlyList<uint>>();
	}

	private static int GetItemColor(BaseGameItemInfo item)
	{
		if (item.ItemType == ItemTypeEnum.TYPE_WEAPON)
		{
			return GameData.WeaponData.Values.FirstOrDefault((WeaponExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == item.TemplateId)?.Color ?? 0;
		}
		if (item.ItemType == ItemTypeEnum.TYPE_SUPPORT)
		{
			return (int)(GameData.SupportCardData.FirstOrDefault((SupportCardExcel x) => x.TemplateId == item.TemplateId)?.Color ?? 0);
		}
		return 0;
	}

	private static Dictionary<int, ulong> BuildLevelExpTable(IEnumerable<(int Lv, uint NeedExp)> source)
	{
		Dictionary<int, ulong> dictionary = new Dictionary<int, ulong>();
		ulong num = 0uL;
		foreach (var item3 in source.OrderBy(((int Lv, uint NeedExp) x) => x.Lv))
		{
			int item = item3.Lv;
			uint item2 = item3.NeedExp;
			dictionary[item] = num;
			num += item2;
		}
		return dictionary;
	}

	private static uint GetUInt(JToken? token)
	{
		JTokenType? jTokenType = token?.Type;
		if (1 == 0)
		{
		}
		uint result2;
		switch (jTokenType)
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

	private static decimal GetDecimal(JToken? token)
	{
		JTokenType? jTokenType = token?.Type;
		if (1 == 0)
		{
		}
		decimal result2;
		switch (jTokenType)
		{
		case JTokenType.Integer:
			result2 = token.Value<decimal>();
			break;
		case JTokenType.Float:
			result2 = token.Value<decimal>();
			break;
		case JTokenType.String:
		{
			if (!decimal.TryParse(token.Value<string>(), out var result))
			{
				goto default;
			}
			result2 = result;
			break;
		}
		default:
			result2 = 0m;
			break;
		}
		if (1 == 0)
		{
		}
		return result2;
	}
}
