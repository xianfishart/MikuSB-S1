using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Reward;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

internal static class FishingRules
{
	public const uint Gid = 32u;

	public const uint ExpSid = 1u;

	private const uint FishBookBase = 20000u;

	public static uint GetFishingLevel(PlayerInstance player)
	{
		uint num = player.GetAttr(32u, 1u);
		uint result = 1u;
		foreach (FishingLevelExcel item in GameData.FishingLevelData.Values.OrderBy((FishingLevelExcel x) => x.Level))
		{
			if (item.UpperExp != 0)
			{
				if (num < item.UpperExp)
				{
					result = item.Level;
					break;
				}
				num -= item.UpperExp;
				result = item.Level;
			}
		}
		return result;
	}

	public static uint PickFish(PlayerInstance player, FishingNodeExcel node)
	{
		if (!(node.FishWeight is JArray { Count: not 0 } jArray))
		{
			return 0u;
		}
		List<(uint, double)> list = new List<(uint, double)>();
		foreach (JArray item3 in jArray.OfType<JArray>())
		{
			if (item3.Count >= 2 && uint.TryParse(item3[0]?.ToString(), out var result) && result != 0 && GameData.FishingFishData.TryGetValue(result, out FishingFishExcel value) && value.Level <= node.FishingLevel + 5)
			{
				double.TryParse(item3[1]?.ToString(), out var result2);
				if (!(result2 <= 0.0))
				{
					list.Add((result, result2));
				}
			}
		}
		if (list.Count == 0)
		{
			return 0u;
		}
		double num = list.Sum(((uint FishId, double Weight) x) => x.Weight);
		double num2 = Random.Shared.NextDouble() * num;
		foreach (var item4 in list)
		{
			uint item = item4.Item1;
			double item2 = item4.Item2;
			num2 -= item2;
			if (num2 <= 0.0)
			{
				return item;
			}
		}
		return list[list.Count - 1].Item1;
	}

	public static uint Roll(JToken? table, uint level)
	{
		if (!(table is JArray { Count: not 0 } jArray))
		{
			return 0u;
		}
		JArray jArray2 = null;
		foreach (JArray item in jArray.OfType<JArray>())
		{
			if (item.Count >= 3 && uint.TryParse(item[0]?.ToString(), out var result) && result <= level)
			{
				jArray2 = item;
			}
		}
		if (jArray2 == null)
		{
			jArray2 = jArray.OfType<JArray>().FirstOrDefault((JArray x) => x.Count >= 3);
		}
		if (jArray2 == null)
		{
			return 0u;
		}
		uint.TryParse(jArray2[1]?.ToString(), out var result2);
		uint.TryParse(jArray2[2]?.ToString(), out var result3);
		if (result3 <= result2)
		{
			return result2;
		}
		return (uint)Random.Shared.NextInt64(result2, result3 + 1);
	}

	public static uint UpdateBook(PlayerInstance player, FishingFishExcel fish, uint length, uint weight, NtfSyncPlayer sync)
	{
		uint num = 20000 + fish.Id * 10;
		PlayerAttr orCreate = player.Attributes.GetOrCreate(32u, num + 1);
		orCreate.Val++;
		player.Attributes.SyncTo(sync, orCreate);
		UpdateMax(player, num + 2, length, sync);
		UpdateMax(player, num + 3, weight, sync);
		return orCreate.Val;
	}

	public static IEnumerable<List<uint>> MeatRows(FishingFishExcel fish)
	{
		JToken value = fish.ConvertFishMeat;
		if (!(value is JObject obj))
		{
			yield break;
		}
		foreach (KeyValuePair<string, JToken> item in obj)
		{
			item.Deconstruct(out var _, out value);
			JToken value2 = value;
			foreach (List<uint> item2 in RewardText.Parse(value2))
			{
				yield return item2;
			}
		}
	}

	private static void UpdateMax(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(32u, sid);
		if (value > orCreate.Val)
		{
			orCreate.Val = value;
			player.Attributes.SyncTo(sync, orCreate);
		}
	}
}
