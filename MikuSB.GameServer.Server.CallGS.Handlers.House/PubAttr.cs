using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class PubAttr
{
	private const uint InfoStart = 16000u;

	private const uint ExpOffset = 1u;

	private const uint LevelRewardGot1Offset = 2u;

	private const uint LevelRewardGot2Offset = 3u;

	private static Dictionary<uint, (uint Exp, List<uint> Reward)>? _ladder;

	private const uint HistoryDataStartOffset = 10u;

	private const uint HistoryDataOffset = 10u;

	private const uint MaxHistoryDataType = 9u;

	public static uint Sid(uint offset)
	{
		return 16000 + offset;
	}

	public static Dictionary<uint, (uint Exp, List<uint> Reward)> Ladder()
	{
		if (_ladder != null)
		{
			return _ladder;
		}
		_ladder = new Dictionary<uint, (uint, List<uint>)>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "GuessPoker";
			buffer[4] = "GuessPokerRewards.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return _ladder;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return _ladder;
			}
			foreach (JsonNode item2 in jsonArray)
			{
				if (!(item2 is JsonObject jsonObject))
				{
					continue;
				}
				uint num = (uint)Num(jsonObject["Level"]);
				uint item = (uint)Num(jsonObject["Exp"]);
				List<uint> list = new List<uint>();
				if (jsonObject["Reward"] is JsonArray jsonArray2)
				{
					foreach (JsonNode item3 in jsonArray2)
					{
						list.Add((uint)Math.Max(0, Num(item3)));
					}
				}
				if (num != 0)
				{
					_ladder[num] = (item, list);
				}
			}
		}
		catch
		{
		}
		return _ladder;
	}

	public static uint ResolveLevel(uint exp)
	{
		Dictionary<uint, (uint, List<uint>)> dictionary = Ladder();
		if (dictionary.Count == 0)
		{
			return 1u;
		}
		uint num = dictionary.Keys.Max();
		uint result = 1u;
		uint num2 = exp;
		(uint, List<uint>) value;
		for (uint num3 = 1u; num3 <= num && dictionary.TryGetValue(num3, out value); num3++)
		{
			uint num4 = ((num3 != num) ? value.Item1 : 0u);
			if (num2 < num4)
			{
				break;
			}
			num2 -= num4;
			result = ((num3 == num) ? num : (num3 + 1));
		}
		return result;
	}

	public static bool CheckLevelRewardGot(PlayerInstance player, uint level)
	{
		uint num2;
		uint offset;
		switch (level)
		{
		case 0u:
			return false;
		default:
		{
			uint num = level - 30;
			num2 = num;
			offset = 3u;
			break;
		}
		case 1u:
		case 2u:
		case 3u:
		case 4u:
		case 5u:
		case 6u:
		case 7u:
		case 8u:
		case 9u:
		case 10u:
		case 11u:
		case 12u:
		case 13u:
		case 14u:
		case 15u:
		case 16u:
		case 17u:
		case 18u:
		case 19u:
		case 20u:
		case 21u:
		case 22u:
		case 23u:
		case 24u:
		case 25u:
		case 26u:
		case 27u:
		case 28u:
		case 29u:
		case 30u:
		{
			uint num = level;
			num2 = num;
			offset = 2u;
			break;
		}
		}
		uint attr = player.GetAttr(101u, Sid(offset));
		return ((attr >> (int)num2) & 1) != 0;
	}

	public static Task SetLevelRewardGotAsync(Connection connection, PlayerInstance player, uint level, NtfSyncPlayer sync)
	{
		uint num2;
		uint offset;
		if (level > 30)
		{
			uint num = level - 30;
			num2 = num;
			offset = 3u;
		}
		else
		{
			uint num = level;
			num2 = num;
			offset = 2u;
		}
		uint sid = Sid(offset);
		uint attr = player.GetAttr(101u, sid);
		return SetAttrAsync(connection, sid, attr | (uint)(1 << (int)num2), sync);
	}

	public static Task SetAttrAsync(Connection connection, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = playerAttr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.CompletedTask;
	}

	public static uint ResolveExp(JsonObject request, bool guide)
	{
		int num = Num(request["nModeType"]);
		int num2 = Num(request["nPlayerNum"]);
		int num3 = Num(request["nRank"]);
		if (num == 0 || num2 == 0 || num3 == 0)
		{
			return 0u;
		}
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "house";
			buffer[3] = "GuessPoker";
			buffer[4] = "GuessPokerMode.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return 0u;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return 0u;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || Num(jsonObject["ModeType"]) != num || Num(jsonObject["PlayerNum"]) != num2)
				{
					continue;
				}
				if (!(jsonObject[guide ? "TrialExp" : "RankExp"] is JsonArray { Count: not 0 } jsonArray2))
				{
					return 0u;
				}
				int index = Math.Min(num3, jsonArray2.Count) - 1;
				return (uint)Math.Max(0, Num(jsonArray2[index]));
			}
		}
		catch
		{
		}
		return 0u;
	}

	public static Task UpdateHistoryAsync(Connection connection, PlayerInstance player, JsonObject request, bool isWin, uint rank, NtfSyncPlayer sync)
	{
		uint num = (uint)Math.Max(0, Num(request["nModeType"]));
		uint num2 = (uint)Math.Max(0, Num(request["nPlayerNum"]));
		if (num == 0 || num2 == 0)
		{
			return Task.CompletedTask;
		}
		uint num3 = (num - 1) * 3 + num2 - 1;
		if (num3 == 0 || num3 > 9)
		{
			return Task.CompletedTask;
		}
		uint num4 = Sid(10 + (num3 - 1) * 10);
		JsonObject jsonObject = request["tbData"] as JsonObject;
		uint sid = num4 + 1;
		uint attr = player.GetAttr(101u, sid);
		uint num5 = Math.Min(attr & 0xFFFF, 65534u) + 1;
		uint num6 = (attr >> 16) & 0xFFFF;
		if (isWin)
		{
			num6 = Math.Min(num6, 65534u) + 1;
		}
		SetAttr(connection, player, sid, (num6 << 16) | num5, sync);
		uint sid2 = num4 + 2;
		uint attr2 = player.GetAttr(101u, sid2);
		uint val = attr2 & 0xFFFF;
		uint val2 = (attr2 >> 16) & 0xFFFF;
		if (isWin)
		{
			val = Math.Min(val, 65534u) + 1;
			val2 = 0u;
		}
		else
		{
			val2 = Math.Min(val2, 65534u) + 1;
			val = 0u;
		}
		SetAttr(connection, player, sid2, (val2 << 16) | val, sync);
		uint num7 = ((jsonObject != null) ? ((uint)Math.Max(0, Num(jsonObject["nScore"]))) : 0u);
		uint sid3 = num4 + 3;
		if (num7 > player.GetAttr(101u, sid3))
		{
			SetAttr(connection, player, sid3, num7, sync);
		}
		FoldAverage(connection, player, num4 + 4, rank * 100, attr & 0xFFFF, sync);
		FoldAverage(connection, player, num4 + 5, (jsonObject != null) ? ((uint)Math.Max(0, Num(jsonObject["nDoubt"]))) : 0u, attr & 0xFFFF, sync);
		FoldAverage(connection, player, num4 + 6, (jsonObject != null) ? ((uint)Math.Max(0, Num(jsonObject["nBeDoubt"]))) : 0u, attr & 0xFFFF, sync);
		FoldAverage(connection, player, num4 + 7, (jsonObject != null) ? ((uint)Math.Max(0, Num(jsonObject["nAveragePlayNum"]))) : 0u, attr & 0xFFFF, sync);
		FoldAverage(connection, player, num4 + 8, (jsonObject != null) ? ((uint)Math.Max(0, Num(jsonObject["nDrinkTrueRate"]))) : 0u, attr & 0xFFFF, sync);
		return Task.CompletedTask;
	}

	private static void FoldAverage(Connection connection, PlayerInstance player, uint sid, uint value, uint previousGames, NtfSyncPlayer sync)
	{
		uint attr = player.GetAttr(101u, sid);
		uint value2 = (uint)((previousGames == 0) ? value : ((ulong)((long)attr * (long)previousGames + value) / (ulong)(previousGames + 1)));
		SetAttr(connection, player, sid, value2, sync);
	}

	private static void SetAttr(Connection connection, PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 101 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 101u,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(101u, sid)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(101u, sid)] = playerAttr.Val;
	}

	public static int Num(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
