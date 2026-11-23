using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Game.Reward;

public static class RewardText
{
	public static List<List<uint>> Parse(JToken? token)
	{
		List<List<uint>> list = new List<List<uint>>();
		if (!(token is JArray jArray))
		{
			return list;
		}
		foreach (JToken item in jArray)
		{
			List<uint> list2 = ParseOne(item);
			if (list2 != null)
			{
				list.Add(list2);
			}
		}
		return list;
	}

	public static List<List<uint>> Parse(IEnumerable<string> texts)
	{
		List<List<uint>> list = new List<List<uint>>();
		foreach (string text in texts)
		{
			List<uint> list2 = ParseOne(text);
			if (list2 != null)
			{
				list.Add(list2);
			}
		}
		return list;
	}

	public static List<uint>? ParseOne(JToken node)
	{
		if (node.Type == JTokenType.Array)
		{
			List<uint> list = new List<uint>();
			foreach (JToken item in (IEnumerable<JToken>)node)
			{
				if (!uint.TryParse(item.ToString(), out var result))
				{
					return null;
				}
				list.Add(result);
			}
			return (list.Count >= 5 && list[4] != 0) ? list : null;
		}
		return ParseOne(node.ToString());
	}

	public static List<uint>? ParseOne(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] array = text.Trim().Trim(new char[3] { '[', ']', '"' }).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length < 5)
		{
			return null;
		}
		List<uint> list = new List<uint>(5);
		foreach (string item in array.Take(5))
		{
			if (!uint.TryParse(item, out var result))
			{
				return null;
			}
			list.Add(result);
		}
		return (list[4] != 0) ? list : null;
	}
}
