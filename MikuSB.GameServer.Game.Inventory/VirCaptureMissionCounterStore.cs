using System;
using System.Collections.Generic;
using MikuSB.Database;

namespace MikuSB.GameServer.Game.Inventory;

/// <summary>开拓目标计数器的读写入口（第 343 节）。写法与 VirCaptureCardSkillStore 一致。</summary>
public static class VirCaptureMissionCounterStore
{
	private const int CacheUid = 1;

	private static readonly object Gate = new object();

	private static VirCaptureMissionCounterData? _cache;

	private static bool _tableReady;

	public static VirCaptureMissionCounterData Get(int uid)
	{
		lock (Gate)
		{
			if (_cache != null && _cache.Uid == uid)
			{
				return _cache;
			}
		}
		if (!_tableReady)
		{
			lock (Gate)
			{
				if (!_tableReady)
				{
					try
					{
						DatabaseHelper.InitializeSqliteTable<VirCaptureMissionCounterData>();
					}
					catch (Exception)
					{
					}
					_tableReady = true;
				}
			}
		}
		VirCaptureMissionCounterData data = DatabaseHelper.GetInstanceOrCreateNew<VirCaptureMissionCounterData>(uid);
		if (data.Counters == null)
		{
			data.Counters = new Dictionary<string, long>();
		}
		if (data.ClearedLevels == null)
		{
			data.ClearedLevels = new List<uint>();
		}
		lock (Gate)
		{
			_cache = data;
		}
		return data;
	}

	public static long Add(int uid, string key, long amount)
	{
		if (string.IsNullOrEmpty(key) || amount == 0)
		{
			return 0L;
		}
		VirCaptureMissionCounterData data = Get(uid);
		data.Counters.TryGetValue(key, out long value);
		value += amount;
		if (value < 0L)
		{
			value = 0L;
		}
		data.Counters[key] = value;
		return value;
	}

	/// <summary>只涨不落的计数器（评分、金币存量这类"最高值"）。</summary>
	public static long RaiseTo(int uid, string key, long value)
	{
		if (string.IsNullOrEmpty(key))
		{
			return 0L;
		}
		VirCaptureMissionCounterData data = Get(uid);
		data.Counters.TryGetValue(key, out long old);
		if (value > old)
		{
			data.Counters[key] = value;
			return value;
		}
		return old;
	}

	public static long Peek(int uid, string key)
	{
		VirCaptureMissionCounterData data = Get(uid);
		long value;
		return data.Counters.TryGetValue(key, out value) ? value : 0L;
	}

	public static void MarkLevelCleared(int uid, uint levelId)
	{
		if (levelId == 0)
		{
			return;
		}
		VirCaptureMissionCounterData data = Get(uid);
		if (!data.ClearedLevels.Contains(levelId))
		{
			data.ClearedLevels.Add(levelId);
		}
	}

	public static bool IsLevelCleared(int uid, uint levelId)
	{
		return levelId != 0 && Get(uid).ClearedLevels.Contains(levelId);
	}

	public static void Save(int uid)
	{
		DatabaseHelper.SaveDatabaseType(Get(uid));
	}

	/// <summary>缓存里的对象在账号切换时要丢弃，否则会拿到上一个号的数据。</summary>
	public static void ResetCache()
	{
		lock (Gate)
		{
			_cache = null;
		}
	}

	public static int CacheKey => CacheUid;
}
