using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Dlc;

public static class DlcRuntimeOverrides
{
	public static uint EffectiveDlcId { get; set; } = 1u;

	public static DateTime? ParseConfigTime(string? raw)
	{
		return DlcEraState.ParseConfigTime(raw);
	}

	public static long ToUnixSeconds(DateTime? time)
	{
		long result;
		if (time.HasValue)
		{
			DateTime valueOrDefault = time.GetValueOrDefault();
			result = new DateTimeOffset(valueOrDefault).ToUnixTimeSeconds();
		}
		else
		{
			result = 0L;
		}
		return result;
	}

	public static long ToUnixSeconds(DateTime time)
	{
		return new DateTimeOffset(time).ToUnixTimeSeconds();
	}

	public static List<uint[]> ReadRewardRows(JToken? token)
	{
		List<uint[]> list = new List<uint[]>();
		if (!(token is JArray jArray))
		{
			return list;
		}
		foreach (JToken item in jArray)
		{
			if (item is JArray { Count: >=5 } jArray2)
			{
				uint[] array = new uint[5];
				for (int i = 0; i < 5; i++)
				{
					array[i] = ToUint(jArray2[i]);
				}
				if (array[0] != 0 && array[4] != 0)
				{
					list.Add(array);
				}
			}
		}
		return list;
	}

	private static uint ToUint(JToken? token)
	{
		uint result;
		return (token != null && uint.TryParse(token.ToString(), out result)) ? result : 0u;
	}

	public static long ActivationFloor()
	{
		return (from x in GameData.DlcActivityData.Values
			select ToUnixSeconds(ParseConfigTime(x.EnterStartTime)) into x
			where x > 0
			select x).DefaultIfEmpty(0L).Max();
	}

	public static bool IsHistoricalWindow(string? windowEndRaw)
	{
		long num = ActivationFloor();
		if (num <= 0)
		{
			return false;
		}
		long num2 = ToUnixSeconds(ParseConfigTime(windowEndRaw));
		return num2 > 0 && num2 < num;
	}
}
