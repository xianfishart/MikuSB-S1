using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

internal static class RepressionState
{
	private const uint GroupId = 31u;

	private const uint LevelFlagGid = 21u;

	private const uint LevelPassGid = 22u;

	private const uint CurTeamBase = 27u;

	private const uint RecTeamBase = 200u;

	private const uint RecTeamStride = 15u;

	private const uint ChapterScoreBase = 100u;

	private const uint TotalScoreSid = 3u;

	private const uint FirstEnterSid = 6u;

	private const uint BossBestBase = 1000u;

	private const int TypeKill = 2;

	private const int TypeHp = 3;

	private const int TypeDamage = 4;

	private const int TypePassNum = 5;

	private static JsonArray? _chapters;

	private static JsonArray? _levels;

	public static void WriteNum(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 31 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 31u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(31u, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(31u, sid)] = value;
	}

	public static uint ReadNum(PlayerInstance player, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 31 && x.Sid == sid)?.Val ?? 0;
	}

	public static void RecordFormation(PlayerInstance player, JsonArray? formation, NtfSyncPlayer sync)
	{
		if (formation == null)
		{
			return;
		}
		bool value = default;
		for (int i = 1; i <= 3 && i <= formation.Count; i++)
		{
			JsonArray jsonArray = formation[i - 1] as JsonArray;
			uint num = ((jsonArray != null && jsonArray.Count > 0) ? ReadUInt(jsonArray[0]) : 0u);
			bool flag = (jsonArray != null && jsonArray.Count > 1 && jsonArray[1] is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value)) & value;
			if (num != 0 && num < 10000)
			{
				flag = true;
			}
			WriteNum(player, (uint)(27 + (i - 1) * 5), num, sync);
			WriteNum(player, (uint)(27 + (i - 1) * 5 + 1), flag ? 1u : 0u, sync);
			for (uint num2 = 2u; num2 < 5; num2++)
			{
				WriteNum(player, (uint)(27 + (i - 1) * 5) + num2, 0u, sync);
			}
		}
	}

	public static uint Settle(PlayerInstance player, uint levelId, JsonObject? param, NtfSyncPlayer sync)
	{
		uint num = ReadGid(player, 22u, levelId);
		uint value = ((num == 0 || num >= 1000) ? 1u : (num + 1));
		WriteGid(player, 22u, levelId, value, sync);
		uint num2 = ReadGid(player, 21u, levelId);
		num2 |= 1;
		num2 |= 0x100;
		WriteGid(player, 21u, levelId, num2, sync);
		uint num3 = ComputeLevelScore(levelId, param);
		JsonObject jsonObject = FindChapter(levelId);
		if (jsonObject == null)
		{
			return num3;
		}
		int num4 = (int)ReadUInt(jsonObject["ID"]);
		int num5 = (int)ReadUInt(jsonObject["Difficult"]);
		if (num4 <= 0 || num5 <= 0)
		{
			return num3;
		}
		uint num6 = (uint)((num5 - 1) * 10 + num4 - 1);
		WriteNum(player, 6u, ReadUInt(jsonObject["AID"]), sync);
		bool flag = jsonObject["Level"] is JsonArray { Count: >0 } jsonArray && ReadUInt(jsonArray[jsonArray.Count - 1]) == levelId;
		uint num7 = ReadNum(player, 1000 + num6);
		if (flag && num3 > num7)
		{
			num7 = num3;
			WriteNum(player, 1000 + num6, num7, sync);
		}
		uint num8 = num7;
		if (jsonObject["Level"] is JsonArray jsonArray2)
		{
			foreach (JsonNode item in jsonArray2)
			{
				uint num9 = ReadUInt(item);
				if (num9 != 0 && num9 != levelId && ReadGid(player, 22u, num9) != 0)
				{
					num8 += BaseScore(num9);
				}
			}
		}
		WriteNum(player, 100 + num6, num8, sync);
		uint value2 = ReadNum(player, 3u) + num3;
		WriteNum(player, 3u, value2, sync);
		uint num10 = 200 + num6 * 15;
		for (uint num11 = 0u; num11 < 15; num11++)
		{
			WriteNum(player, num10 + num11, ReadNum(player, 27 + num11), sync);
		}
		return num3;
	}

	private static uint ComputeLevelScore(uint levelId, JsonObject? param)
	{
		JsonObject jsonObject = FindLevel(levelId);
		if (jsonObject == null)
		{
			return 0u;
		}
		if (!(jsonObject["Score"] is JsonArray jsonArray))
		{
			return 0u;
		}
		if (jsonArray.Count == 0 || jsonArray[0] is JsonValue)
		{
			return BaseScore(levelId);
		}
		double num = 0.0;
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonArray { Count: >=2 } jsonArray2)
			{
				int type = (int)ReadUInt(jsonArray2[0]);
				double value = ReadConditionValue(param, type);
				num += Interpolate(jsonArray2, value);
			}
		}
		return (uint)Math.Max(0.0, Math.Round(num));
	}

	private static double ReadConditionValue(JsonObject? param, int type)
	{
		if (param == null)
		{
			return 0.0;
		}
		switch (type)
		{
		case 2:
		{
			double num3 = 0.0;
			if (param["tbKill"] is JsonObject jsonObject)
			{
				foreach (KeyValuePair<string, JsonNode> item in jsonObject)
				{
					num3 += ReadDouble(item.Value);
				}
			}
			else if (param["tbKill"] is JsonArray jsonArray2)
			{
				foreach (JsonNode item2 in jsonArray2)
				{
					num3 += ReadDouble(item2);
				}
			}
			return num3;
		}
		case 3:
		{
			double num = 0.0;
			int num2 = 0;
			if (param["tbHP"] is JsonArray jsonArray)
			{
				foreach (JsonNode item3 in jsonArray)
				{
					num += ReadDouble(item3);
					num2++;
				}
			}
			return (num2 > 0) ? Math.Round(num / (double)num2 * 100.0, 1) : 0.0;
		}
		case 4:
			return ReadDouble(param["nTotalDamage"]);
		case 5:
			return ReadDouble(param["nPassNum"]);
		default:
			return 0.0;
		}
	}

	private static double Interpolate(JsonArray rule, double value)
	{
		List<(double, double)> list = new List<(double, double)>();
		for (int i = 1; i < rule.Count; i++)
		{
			if (rule[i] is JsonArray { Count: >=2 } jsonArray)
			{
				list.Add((ReadDouble(jsonArray[0]), ReadDouble(jsonArray[1])));
			}
		}
		if (list.Count == 0)
		{
			return 0.0;
		}
		if (value <= list[0].Item1)
		{
			return list[0].Item2;
		}
		for (int j = 1; j < list.Count; j++)
		{
			if (!(value > list[j].Item1))
			{
				var (num, num2) = list[j - 1];
				var (num3, num4) = list[j];
				if (num3 <= num)
				{
					return num4;
				}
				double num5 = (value - num) / (num3 - num);
				return num2 + (num4 - num2) * num5;
			}
		}
		return list[list.Count - 1].Item2;
	}

	private static uint BaseScore(uint levelId)
	{
		if (!(FindLevel(levelId)?["Score"] is JsonArray { Count: not 0 } jsonArray))
		{
			return 0u;
		}
		return (jsonArray[0] is JsonValue) ? ReadUInt(jsonArray[0]) : FirstNumber(jsonArray[0]);
	}

	public static int ResolveActId(PlayerInstance player)
	{
		foreach (JsonNode item in Chapters())
		{
			if (!(item is JsonObject jsonObject) || !(jsonObject["Level"] is JsonArray jsonArray))
			{
				continue;
			}
			foreach (JsonNode item2 in jsonArray)
			{
				uint num = ReadUInt(item2);
				if (num != 0 && ReadGid(player, 22u, num) != 0)
				{
					return (int)ReadUInt(jsonObject["AID"]);
				}
			}
		}
		return 0;
	}

	private static uint ReadGid(PlayerInstance player, uint gid, uint sid)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid)?.Val ?? 0;
	}

	private static void WriteGid(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}

	private static JsonArray Chapters()
	{
		if (_chapters != null)
		{
			return _chapters;
		}
		_chapters = new JsonArray();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "Repression";
			buffer[4] = "chapter.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject)
					{
						_chapters.Add(jsonObject.DeepClone());
					}
				}
			}
		}
		catch
		{
		}
		return _chapters;
	}

	private static JsonArray Levels()
	{
		if (_levels != null)
		{
			return _levels;
		}
		_levels = new JsonArray();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "Repression";
			buffer[4] = "level.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					if (item is JsonObject jsonObject)
					{
						_levels.Add(jsonObject.DeepClone());
					}
				}
			}
		}
		catch
		{
		}
		return _levels;
	}

	private static JsonObject? FindLevel(uint levelId)
	{
		foreach (JsonNode item in Levels())
		{
			if (!(item is JsonObject jsonObject) || ReadUInt(jsonObject["ID"]) != levelId)
			{
				continue;
			}
			return jsonObject;
		}
		return null;
	}

	private static JsonObject? FindChapter(uint levelId)
	{
		foreach (JsonNode item in Chapters())
		{
			if (!(item is JsonObject jsonObject) || !(jsonObject["Level"] is JsonArray jsonArray))
			{
				continue;
			}
			foreach (JsonNode item2 in jsonArray)
			{
				if (ReadUInt(item2) == levelId)
				{
					return jsonObject;
				}
			}
		}
		return null;
	}

	private static uint FirstNumber(JsonNode? node)
	{
		if (!(node is JsonValue node2))
		{
			if (node is JsonArray jsonArray)
			{
				foreach (JsonNode item in jsonArray)
				{
					uint num = FirstNumber(item);
					if (num != 0)
					{
						return num;
					}
				}
				return 0u;
			}
			return 0u;
		}
		return (uint)Math.Max(0.0, ReadDouble(node2));
	}

	private static uint ReadUInt(JsonNode? node)
	{
		return (uint)Math.Max(0.0, ReadDouble(node));
	}

	private static double ReadDouble(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<double>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return value2;
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return value3;
			}
		}
		return 0.0;
	}
}
