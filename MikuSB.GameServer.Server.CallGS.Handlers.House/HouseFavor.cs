using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

public static class HouseFavor
{
	/// <summary>
	/// 2026-11-13 追加（修复记录第 283 节）：基地「氛围值」（房屋好感）属性位。
	/// 客户端 HouseStorage 常量：HouseInfoStart=3000、HouseInfoOffset.HouseLoveLevel=100，
	/// 读等级 GetBits(val,0,10)（低 11 位）、点数 GetBits(val,11,30)（高位 20 位），
	/// 与少女好感的打包方式完全一致；升级需求表为 house_level.json（29 级，NeedPoint）。
	/// </summary>
	internal const uint HouseLoveSid = 3100u;

	private static List<(int Level, int Need)>? _levels;

	private static List<(int Level, int Need)>? _houseLevels;

	public static async Task<(uint Added, int PreLevel, int NewLevel)> Award(Connection connection, int girlId, uint points, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null || girlId <= 0 || points == 0)
		{
			return (Added: 0u, PreLevel: 0, NewLevel: 0);
		}
		uint sid = (uint)(girlId * 50);
		uint current = HouseAttr.Read(player, sid);
		int level = (int)(current & 0x7FF);
		if (level == 0)
		{
			level = 1;
		}
		int point = (int)((current >> 11) & 0xFFFFF);
		int preLevel = level;
		List<(int Level, int Need)> levels = LoadLevels();
		for (point = Math.Min(point + (int)points, 30000); level < 50 && point >= Need(levels, level); level++)
		{
			point -= Need(levels, level);
		}
		uint packed = (uint)(level | (point << 11));
		// sendImmediate: 客户端好感条读本地属性缓存，只走 1004 同步包不会立刻刷新，必须补 1011。
		await HouseAttr.SetAsync(connection, sid, packed, sync, false, sendImmediate: true);
		return (Added: points, PreLevel: preLevel, NewLevel: level);
	}

	/// <summary>
	/// 2026-11-13 追加（修复记录第 283 节）：加基地氛围值（gift.json 的 AddHouseLoveNum）。
	/// 旧版本只处理少女好感，送「氛围值礼物」（如 GiftId 221 / 2011，AddHouseLoveNum=4000）
	/// 时整段逻辑被跳过 → 界面毫无变化。
	/// </summary>
	public static async Task<(uint Added, int PreLevel, int NewLevel)> AwardHouse(Connection connection, uint points, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null || points == 0)
		{
			return (Added: 0u, PreLevel: 0, NewLevel: 0);
		}
		uint current = HouseAttr.Read(player, HouseLoveSid);
		int level = (int)(current & 0x7FF);
		if (level <= 0)
		{
			level = 1;
		}
		int point = (int)((current >> 11) & 0xFFFFF);
		int preLevel = level;
		List<(int Level, int Need)> levels = LoadHouseLevels();
		int maxLevel = ((levels.Count > 0) ? levels[levels.Count - 1].Level : 29);
		for (point = Math.Min(point + (int)points, 0xFFFFF); level < maxLevel && point >= Need(levels, level); level++)
		{
			point -= Need(levels, level);
		}
		uint packed = (uint)((level & 0x7FF) | (Math.Min(point, 0xFFFFF) << 11));
		// sendImmediate: 同上，基地氛围值界面也读本地属性缓存。
		await HouseAttr.SetAsync(connection, HouseLoveSid, packed, sync, false, sendImmediate: true);
		return (Added: points, PreLevel: preLevel, NewLevel: level);
	}

	/// <summary>基地氛围值变化的回包字段（与少女好感分开，避免客户端把两者混在一起）。</summary>
	public static void AddHouseFavorFields(JsonObject response, int preLevel, int newLevel, uint added)
	{
		if (added != 0)
		{
			response["AddHouseFavor"] = added;
			response["HousePreLevel"] = preLevel;
			if (newLevel > preLevel)
			{
				response["HouseNewLevel"] = newLevel;
			}
		}
	}

	public static void AddFavorFields(JsonObject response, int preLevel, int newLevel, uint added)
	{
		if (added != 0)
		{
			response["AddFavor"] = added;
			response["PreLevel"] = preLevel;
			if (newLevel > preLevel)
			{
				response["NewLevel"] = newLevel;
			}
		}
	}

	private static int Need(List<(int Level, int Need)> levels, int level)
	{
		(int, int) tuple = levels.FirstOrDefault(((int Level, int Need) x) => x.Level == level);
		return (tuple.Item2 > 0) ? tuple.Item2 : 1000000;
	}

	private static List<(int Level, int Need)> LoadLevels()
	{
		if (_levels != null)
		{
			return _levels;
		}
		_levels = new List<(int, int)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "love_level.json");
			if (File.Exists(path))
			{
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject obj)
						{
							_levels.Add((HouseJson.NumField(obj, "Level"), HouseJson.NumField(obj, "NeedPoint")));
						}
					}
				}
			}
		}
		catch
		{
		}
		return _levels;
	}

	/// <summary>基地等级需求表 Resources/house/house_level.json（Level + NeedPoint，共 29 级）。</summary>
	private static List<(int Level, int Need)> LoadHouseLevels()
	{
		if (_houseLevels != null)
		{
			return _houseLevels;
		}
		_houseLevels = new List<(int, int)>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "house_level.json");
			if (File.Exists(path))
			{
				JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
				if (jsonArray != null)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject obj)
						{
							_houseLevels.Add((HouseJson.NumField(obj, "Level"), HouseJson.NumField(obj, "NeedPoint")));
						}
					}
				}
			}
		}
		catch
		{
		}
		return _houseLevels;
	}
}
