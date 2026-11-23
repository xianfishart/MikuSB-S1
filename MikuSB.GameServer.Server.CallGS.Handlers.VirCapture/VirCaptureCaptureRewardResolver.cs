using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using MikuSB.Data.Excel;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

internal static class VirCaptureCaptureRewardResolver
{
	private sealed class VirCaptureLevelRegionReward
	{
		public int PalType { get; init; }

		public List<uint> Rewards1 { get; init; } = new List<uint>();
	}

	private static readonly Lock CacheLock = new Lock();

	private static readonly Dictionary<string, Dictionary<uint, VirCaptureLevelRegionReward>> RegionCache = new Dictionary<string, Dictionary<uint, VirCaptureLevelRegionReward>>();

	private static readonly Dictionary<string, Dictionary<uint, List<uint>>> BossCache = new Dictionary<string, Dictionary<uint, List<uint>>>();

	/// <summary>
	/// 第 505 节：蓝图岛（newstar_building01）的宝箱/采集点奖励。
	/// 岛上的 region_info.json 把奖励放在 **Rewards2**（Rewards1 全空），
	/// 五元组形式 [[4,1,6,8,200],[4,1,6,9,2]]，旧实现只读 Rewards1，
	/// 加上第 500 节「岛屿只落点位状态、不发道具」早退，开宝箱就什么都没有。
	/// </summary>
	private static readonly Dictionary<string, Dictionary<uint, List<List<uint>>>> IslandRewardCache = new Dictionary<string, Dictionary<uint, List<List<uint>>>>();

	public static List<List<uint>> ResolveIslandRewards(string mapName, uint regionId)
	{
		using (CacheLock.EnterScope())
		{
			if (!IslandRewardCache.TryGetValue(mapName, out Dictionary<uint, List<List<uint>>> value))
			{
				value = new Dictionary<uint, List<List<uint>>>();
				InlineArray5<string> buffer = default;
				buffer[0] = ConfigManager.Config.Path.ResourcePath;
				buffer[1] = "dlc";
				buffer[2] = "vircapture";
				buffer[3] = mapName;
				buffer[4] = "region_info.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					JArray jArray = JArray.Parse(File.ReadAllText(path));
					foreach (JToken item in jArray)
					{
						uint num = ReadUInt(item["Id"]);
						if (num == 0)
						{
							continue;
						}
						List<List<uint>> list = new List<List<uint>>();
						CollectRewardRows(item["Rewards1"], list);
						CollectRewardRows(item["Rewards2"], list);
						CollectRewardRows(item["Rewards3"], list);
						if (list.Count > 0)
						{
							value[num] = list;
						}
					}
				}
				IslandRewardCache[mapName] = value;
			}
			return value.TryGetValue(regionId, out List<List<uint>> rows) ? rows : new List<List<uint>>();
		}
	}

	/// <summary>把 ["五元组"] 或 [["五元组"],["五元组"]] 统一摊平成 List&lt;List&lt;uint&gt;&gt;；4 段 GDPL 补数量 1。</summary>
	private static void CollectRewardRows(JToken? token, List<List<uint>> rows)
	{
		if (token == null || token.Type == JTokenType.Null || token is not JArray array || array.Count == 0)
		{
			return;
		}
		bool flag = true;
		foreach (JToken item in array)
		{
			if (item.Type == JTokenType.Array)
			{
				flag = false;
				break;
			}
			if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float && item.Type != JTokenType.String)
			{
				flag = false;
				break;
			}
		}
		if (flag)
		{
			List<uint> list = new List<uint>();
			foreach (JToken item2 in array)
			{
				list.Add(ReadUInt(item2));
			}
			if (list.Count == 4)
			{
				list.Add(1u);
			}
			if (list.Count >= 5 && list[0] != 0 && list[4] != 0)
			{
				rows.Add(list.GetRange(0, 5));
			}
			return;
		}
		foreach (JToken item3 in array)
		{
			CollectRewardRows(item3, rows);
		}
	}

	public static List<uint>? ResolveGdpl(VirCaptureCaptureRegionExcel captureRegion, uint regionId)
	{
		if (string.IsNullOrWhiteSpace(captureRegion.LevelRegionName))
		{
			return null;
		}
		Dictionary<uint, VirCaptureLevelRegionReward> orLoadRegionMap = GetOrLoadRegionMap(captureRegion.LevelRegionName);
		if (!orLoadRegionMap.TryGetValue(regionId, out var value))
		{
			return null;
		}
		if (value.PalType == 2)
		{
			return GetOrLoadBossMap(captureRegion.LevelRegionName).GetValueOrDefault(regionId);
		}
		return value.Rewards1;
	}

	private static Dictionary<uint, VirCaptureLevelRegionReward> GetOrLoadRegionMap(string mapName)
	{
		using (CacheLock.EnterScope())
		{
			if (RegionCache.TryGetValue(mapName, out Dictionary<uint, VirCaptureLevelRegionReward> value))
			{
				return value;
			}
			Dictionary<uint, VirCaptureLevelRegionReward> dictionary = new Dictionary<uint, VirCaptureLevelRegionReward>();
			InlineArray5<string> buffer = default;
			buffer[0] = ConfigManager.Config.Path.ResourcePath;
			buffer[1] = "dlc";
			buffer[2] = "vircapture";
			buffer[3] = mapName;
			buffer[4] = "region_info.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				JArray jArray = JArray.Parse(File.ReadAllText(path));
				foreach (JToken item in jArray)
				{
					uint num = ReadUInt(item["Id"]);
					if (num != 0)
					{
						dictionary[num] = new VirCaptureLevelRegionReward
						{
							PalType = ReadInt(item["PalType"]),
							// 第 500 节：蓝图岛的 region_info 里 Rewards1 是空字符串 ""（不是数组也不是 null），
							// 直接 ToObject<List<uint>>() 会抛异常 -> handler 崩 -> 客户端弹「请求参数错误」。
							// 统一走容错读取：非数组一律当空列表。
							Rewards1 = ReadUIntList(item["Rewards1"])
						};
					}
				}
			}
			RegionCache[mapName] = dictionary;
			return dictionary;
		}
	}

	private static Dictionary<uint, List<uint>> GetOrLoadBossMap(string mapName)
	{
		using (CacheLock.EnterScope())
		{
			if (BossCache.TryGetValue(mapName, out Dictionary<uint, List<uint>> value))
			{
				return value;
			}
			Dictionary<uint, List<uint>> dictionary = new Dictionary<uint, List<uint>>();
			InlineArray5<string> buffer = default;
			buffer[0] = ConfigManager.Config.Path.ResourcePath;
			buffer[1] = "dlc";
			buffer[2] = "vircapture";
			buffer[3] = mapName;
			buffer[4] = "boss.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				JArray jArray = JArray.Parse(File.ReadAllText(path));
				foreach (JToken item in jArray)
				{
					uint num = ReadUInt(item["RegionId"]);
					List<uint> list = item["Boss"]?.ToObject<List<uint>>();
					if (num != 0 && list != null && list.Count >= 4)
					{
						dictionary.TryAdd(num, list);
					}
				}
			}
			BossCache[mapName] = dictionary;
			return dictionary;
		}
	}

	private static List<uint> ReadUIntList(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null)
		{
			return new List<uint>();
		}
		if (token.Type != JTokenType.Array)
		{
			return new List<uint>();
		}
		try
		{
			return token.ToObject<List<uint>>() ?? new List<uint>();
		}
		catch
		{
			return new List<uint>();
		}
	}

	private static uint ReadUInt(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null)
		{
			return 0u;
		}
		JTokenType type = token.Type;
		if (1 == 0)
		{
		}
		uint result = type switch
		{
			JTokenType.Integer => token.Value<uint>(), 
			JTokenType.Float => Math.Max(0u, (uint)token.Value<double>()), 
			JTokenType.String => uint.TryParse(token.Value<string>(), out var result2) ? result2 : 0u, 
			_ => 0u, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static int ReadInt(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null)
		{
			return 0;
		}
		JTokenType type = token.Type;
		if (1 == 0)
		{
		}
		int result = type switch
		{
			JTokenType.Integer => token.Value<int>(), 
			JTokenType.Float => (int)token.Value<double>(), 
			JTokenType.String => int.TryParse(token.Value<string>(), out var result2) ? result2 : 0, 
			_ => 0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
