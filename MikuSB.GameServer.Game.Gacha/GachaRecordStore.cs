using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MikuSB.GameServer.Game.Gacha;

public static class GachaRecordStore
{
	private static readonly object Gate = new object();

	private static string FileFor(long uid)
	{
		string text = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Database", "GachaRecords");
		Directory.CreateDirectory(text);
		return Path.Combine(text, $"{uid}.json");
	}

	public static void Append(long uid, string key, IEnumerable<uint> gdpl)
	{
		lock (Gate)
		{
			List<GachaRecordEntry> list = Load(uid);
			int order = ((list.Count == 0) ? 1 : (list.Max((GachaRecordEntry x) => x.Order) + 1));
			list.Add(new GachaRecordEntry
			{
				Key = key,
				Gdpl = gdpl.Take(4).ToArray(),
				Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				Order = order
			});
			list = (from x in list
				where DateTimeOffset.UtcNow.ToUnixTimeSeconds() - x.Time <= 7776000
				orderby x.Time descending, x.Order descending
				select x).Take(200).ToList();
			Save(uid, list);
		}
	}

	public static List<GachaRecordEntry> Get(long uid, string key)
	{
		lock (Gate)
		{
			return (from x in Load(uid)
				where string.Equals(x.Key, key, StringComparison.Ordinal)
				orderby x.Time descending, x.Order descending
				select x).ToList();
		}
	}

	private static List<GachaRecordEntry> Load(long uid)
	{
		try
		{
			string path = FileFor(uid);
			if (!File.Exists(path))
			{
				return new List<GachaRecordEntry>();
			}
			return JsonSerializer.Deserialize<List<GachaRecordEntry>>(File.ReadAllText(path)) ?? new List<GachaRecordEntry>();
		}
		catch
		{
			return new List<GachaRecordEntry>();
		}
	}

	private static void Save(long uid, List<GachaRecordEntry> records)
	{
		try
		{
			File.WriteAllText(FileFor(uid), JsonSerializer.Serialize(records));
		}
		catch
		{
		}
	}
}
