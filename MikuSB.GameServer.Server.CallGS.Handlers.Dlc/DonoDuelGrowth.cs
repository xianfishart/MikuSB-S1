using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

internal static class DonoDuelGrowth
{
	public const uint FallbackSeasonCurrency = 63u;

	public const uint AttrAttack = 1u;

	public const uint AttrDefence = 2u;

	public const uint AttrHealth = 3u;

	public const int SkillSlots = 3;

	public const uint ConditionStoryMission = 36u;

	public const uint ConditionArenaLevel = 37u;

	private static readonly Logger Log = new Logger("DonoDuelGrowth");

	public static uint SeasonCurrency(PlayerInstance player)
	{
		uint value = player.Attributes.GetValue(185u, 1u);
		if (value != 0 && GameData.DonoDuelSeasonData.TryGetValue(value, out DonoDuelSeasonExcel value2))
		{
			string s = value2.Currency?.ToString().Trim().Trim(new char[3] { '[', ']', '"' }) ?? string.Empty;
			if (uint.TryParse(s, out var result) && result != 0)
			{
				return result;
			}
		}
		return 63u;
	}

	public static bool ConditionMet(PlayerInstance player, IReadOnlyList<uint> condition)
	{
		if (condition.Count < 2)
		{
			return true;
		}
		uint num = condition[0];
		if (1 == 0)
		{
		}
		bool result = num switch
		{
			36u => DonoDuelTaskState.IsReceived(player, condition[1]), 
			37u => player.Attributes.GetValue(22u, condition[1]) != 0, 
			_ => false, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public static uint AttrCap(uint templateId, uint quality, uint position)
	{
		if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out DonoDuelHeroCardExcel value))
		{
			return 0u;
		}
		if (1 == 0)
		{
		}
		uint result = position switch
		{
			1u => value.AttackCap(quality), 
			2u => value.DefenseCap(quality), 
			_ => value.HealthCap(quality), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public static CallGSResult Bad(string key = "error.BadParam")
	{
		return CallGSResult.Ok(new JsonObject { ["sError"] = key });
	}

	public static CallGSResult Ok(JsonObject? body = null)
	{
		return CallGSResult.Ok(body ?? new JsonObject());
	}

	public static JsonObject? ParseObject(string param)
	{
		try
		{
			return string.IsNullOrWhiteSpace(param) ? null : (JsonNode.Parse(param) as JsonObject);
		}
		catch (Exception ex)
		{
			Log.Warn("Could not parse a Donoduel growth request: " + ex.Message);
			return null;
		}
	}

	public static uint ReadUint(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue))
		{
			return 0u;
		}
		try
		{
			if (jsonValue.TryGetValue<uint>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (value2 >= 0) ? ((uint)value2) : 0u;
			}
			uint result;
			if (jsonValue.TryGetValue<string>(out string value3))
			{
				return uint.TryParse(value3.Trim(), out result) ? result : 0u;
			}
		}
		catch
		{
		}
		return 0u;
	}

	public static bool ReadBool(JsonNode? node)
	{
		if (!(node is JsonValue jsonValue))
		{
			return false;
		}
		try
		{
			if (jsonValue.TryGetValue<bool>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<uint>(out var value2))
			{
				return value2 != 0;
			}
			if (jsonValue.TryGetValue<string>(out string value3))
			{
				return value3.Equals("true", StringComparison.OrdinalIgnoreCase) || value3 == "1";
			}
		}
		catch
		{
		}
		return false;
	}

	public static List<uint> ReadUintList(JsonNode? node)
	{
		List<uint> list = new List<uint>();
		if (!(node is JsonArray jsonArray))
		{
			return list;
		}
		foreach (JsonNode item in jsonArray)
		{
			uint num = ReadUint(item);
			if (num != 0)
			{
				list.Add(num);
			}
		}
		return list;
	}
}
