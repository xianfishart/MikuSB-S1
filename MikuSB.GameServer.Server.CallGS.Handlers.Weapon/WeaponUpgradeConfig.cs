using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

internal sealed class WeaponUpgradeConfig
{
	private readonly Dictionary<int, uint> _weaponNeedExpNormal;

	private readonly Dictionary<int, uint> _weaponNeedExpSsr;

	private readonly Dictionary<int, uint[]> _breakLimits;

	private readonly Dictionary<int, RecycleEntry> _recycleById;

	private readonly Dictionary<ulong, MaterialTemplate> _weaponTemplates;

	private readonly Dictionary<ulong, MaterialTemplate> _suppliesTemplates;

	private readonly Dictionary<int, ulong> _weaponRecycleExpNormal = new Dictionary<int, ulong>();

	private readonly Dictionary<int, ulong> _weaponRecycleExpSsr = new Dictionary<int, ulong>();

	public WeaponUpgradeConfig(Dictionary<int, uint> weaponNeedExpNormal, Dictionary<int, uint> weaponNeedExpSsr, Dictionary<int, uint[]> breakLimits, Dictionary<int, RecycleEntry> recycleById, Dictionary<ulong, MaterialTemplate> weaponTemplates, Dictionary<ulong, MaterialTemplate> suppliesTemplates)
	{
		_weaponNeedExpNormal = weaponNeedExpNormal;
		_weaponNeedExpSsr = weaponNeedExpSsr;
		_breakLimits = breakLimits;
		_recycleById = recycleById;
		_weaponTemplates = weaponTemplates;
		_suppliesTemplates = suppliesTemplates;
		BuildRecycleExpTable(_weaponNeedExpNormal, _weaponRecycleExpNormal);
		BuildRecycleExpTable(_weaponNeedExpSsr, _weaponRecycleExpSsr);
	}

	public static WeaponUpgradeConfig Load()
	{
		Dictionary<int, uint> dictionary = new Dictionary<int, uint>();
		Dictionary<int, uint> dictionary2 = new Dictionary<int, uint>();
		foreach (UpgradeExpExcel value in GameData.UpgradeExpData.Values)
		{
			dictionary[value.Lv] = value.WeaponNeedExp;
			dictionary2[value.Lv] = value.SSRWeaponNeedExp;
		}
		Dictionary<int, uint[]> dictionary3 = new Dictionary<int, uint[]>();
		foreach (BreakLevelLimitExcel value2 in GameData.BreakLevelLimitData.Values)
		{
			dictionary3[value2.ID] = new uint[7] { value2.Break0, value2.Break1, value2.Break2, value2.Break3, value2.Break4, value2.Break5, value2.Break6 };
		}
		Dictionary<int, RecycleEntry> dictionary4 = new Dictionary<int, RecycleEntry>();
		foreach (RecycleExcel value3 in GameData.RecycleData.Values)
		{
			dictionary4[value3.ID] = new RecycleEntry(GetUIntFlexible(value3.RecycleBase), GetDecimalFlexible(value3.RecycleRatio));
		}
		Dictionary<ulong, MaterialTemplate> weaponTemplates = GameData.WeaponData.Values.ToDictionary((WeaponExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level), (WeaponExcel x) => new MaterialTemplate(x.Color, x.ProvideExp, x.ConsumeGold, x.RecycleID, x.BreakLimitID));
		Dictionary<ulong, MaterialTemplate> suppliesTemplates = (from x in GameData.AllSuppliesData
			group x by GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level)).ToDictionary((IGrouping<ulong, SuppliesExcel> g) => g.Key, (IGrouping<ulong, SuppliesExcel> g) => new MaterialTemplate(g.First().Color, g.First().ProvideExp, g.First().ConsumeGold, 0, 0));
		return new WeaponUpgradeConfig(dictionary, dictionary2, dictionary3, dictionary4, weaponTemplates, suppliesTemplates);
	}

	public bool TryGetWeaponTemplate(ulong templateId, out MaterialTemplate template)
	{
		return _weaponTemplates.TryGetValue(templateId, out template);
	}

	public bool TryGetSuppliesTemplate(ulong templateId, out MaterialTemplate template)
	{
		return _suppliesTemplates.TryGetValue(templateId, out template);
	}

	public bool TryGetMaterialGain(BaseGameItemInfo item, out ulong exp)
	{
		exp = 0uL;
		if (TryGetWeaponTemplate(item.TemplateId, out var template))
		{
			exp = template.ProvideExp;
			if (item is GameWeaponInfo { Level: >1u } gameWeaponInfo)
			{
				exp += GetWeaponRecycleExp(template, gameWeaponInfo.Level);
			}
			return true;
		}
		if (TryGetSuppliesTemplate(item.TemplateId, out var template2))
		{
			exp = template2.ProvideExp;
			return true;
		}
		return false;
	}

	public ulong GetWeaponRecycleExp(MaterialTemplate template, uint level)
	{
		if (template.RecycleId <= 0 || !_recycleById.TryGetValue(template.RecycleId, out var value))
		{
			return 0uL;
		}
		Dictionary<int, ulong> dictionary = ((template.Color == 5) ? _weaponRecycleExpSsr : _weaponRecycleExpNormal);
		ulong valueOrDefault = dictionary.GetValueOrDefault((int)level);
		return (ulong)Math.Floor((decimal)(value.RecycleBase + valueOrDefault) * value.RecycleRatio);
	}

	public uint GetWeaponMaxLevel(int breakLimitId, uint currentBreak)
	{
		if (!_breakLimits.TryGetValue(breakLimitId, out uint[] value) || value.Length == 0)
		{
			return 0u;
		}
		int num = (int)Math.Min(currentBreak, (uint)(value.Length - 1));
		return value[num];
	}

	public (uint Level, uint Exp) ApplyWeaponExp(uint level, uint exp, ulong addExp, int color, uint maxLevel)
	{
		if (addExp == 0)
		{
			return (Level: level, Exp: exp);
		}
		if (maxLevel != 0 && level >= maxLevel)
		{
			return (Level: maxLevel, Exp: checked((uint)(exp + addExp)));
		}
		uint num = level;
		ulong num2 = exp + addExp;
		uint weaponNeedExp = GetWeaponNeedExp(color, num);
		while (weaponNeedExp != 0 && num2 >= weaponNeedExp)
		{
			num2 -= weaponNeedExp;
			num++;
			checked
			{
				if (maxLevel != 0 && num >= maxLevel)
				{
					return (Level: maxLevel, Exp: (uint)num2);
				}
				weaponNeedExp = GetWeaponNeedExp(color, num);
				if (weaponNeedExp == 0)
				{
					return (Level: num, Exp: (uint)num2);
				}
			}
		}
		return (Level: num, Exp: checked((uint)num2));
	}

	private uint GetWeaponNeedExp(int color, uint level)
	{
		return (color == 5) ? _weaponNeedExpSsr.GetValueOrDefault((int)level) : _weaponNeedExpNormal.GetValueOrDefault((int)level);
	}

	private static void BuildRecycleExpTable(Dictionary<int, uint> needExp, Dictionary<int, ulong> recycleExp)
	{
		ulong num = 0uL;
		foreach (int item in needExp.Keys.OrderBy((int x) => x))
		{
			recycleExp[item] = num;
			num += needExp[item];
		}
	}

	private static uint GetUIntFlexible(JToken? token)
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

	private static decimal GetDecimalFlexible(JToken? token)
	{
		if (token == null)
		{
			return 0m;
		}
		JTokenType type = token.Type;
		if (1 == 0)
		{
		}
		decimal result2;
		switch (type)
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
