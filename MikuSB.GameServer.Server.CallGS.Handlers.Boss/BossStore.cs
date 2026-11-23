using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Boss;

internal static class BossStore
{
	public sealed record BossAwardRow(uint Id, uint ScoreCount, JToken? ScoreAward);

	public const uint Gid = 50u;

	private const uint StartSid = 100u;

	private const int SubNum = 10;

	private const uint ScoreOffset = 4u;

	private static Dictionary<uint, uint>? _entryScores;

	private static Dictionary<uint, double[][]>? _bossLevels;

	private static List<BossAwardRow>? _awards;

	public static Dictionary<uint, double[][]> BossLevels()
	{
		if (_bossLevels != null)
		{
			return _bossLevels;
		}
		_bossLevels = new Dictionary<uint, double[][]>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "boss";
			buffer[4] = "boss.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return _bossLevels;
			}
			foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
			{
				uint num = (uint)item.Value<long?>("ID").GetValueOrDefault();
				if (num == 0)
				{
					continue;
				}
				List<double[]> list = new List<double[]>();
				if (item["BossLevel"] is JArray source)
				{
					foreach (JArray item2 in source.OfType<JArray>())
					{
						list.Add(item2.Select((JToken x) => x.Value<double>()).ToArray());
					}
				}
				_bossLevels[num] = list.ToArray();
			}
		}
		catch
		{
		}
		return _bossLevels;
	}

	public static uint ComputeScore(uint levelId, int diff, IEnumerable<uint> entryIds)
	{
		Dictionary<uint, uint> dictionary = EntryScores();
		uint num = 0u;
		foreach (uint entryId in entryIds)
		{
			if (dictionary.TryGetValue(entryId, out var value))
			{
				num += value;
			}
		}
		Dictionary<uint, double[][]> dictionary2 = BossLevels();
		if (dictionary2.TryGetValue(levelId, out var value2) && diff >= 1 && diff <= value2.Length)
		{
			double[] array = value2[diff - 1];
			double num2 = ((array.Length > 2) ? array[2] : 1.0);
			double num3 = ((array.Length > 3) ? array[3] : 0.0);
			return (uint)Math.Floor(((double)num + num3) * num2 + 0.5);
		}
		return num;
	}

	public static Dictionary<uint, uint> EntryScores()
	{
		if (_entryScores != null)
		{
			return _entryScores;
		}
		_entryScores = new Dictionary<uint, uint>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "boss";
			buffer[4] = "entries.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
				{
					uint num = (uint)item.Value<long?>("ID").Value;
					uint value = (uint)item.Value<long?>("Score").Value;
					if (num != 0)
					{
						_entryScores[num] = value;
					}
				}
			}
		}
		catch
		{
		}
		return _entryScores;
	}

	public static List<BossAwardRow> Awards()
	{
		if (_awards != null)
		{
			return _awards;
		}
		_awards = new List<BossAwardRow>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "boss";
			buffer[4] = "boss_award.json";
			string path = Path.Combine(buffer);
			if (File.Exists(path))
			{
				foreach (JToken item in JArray.Parse(File.ReadAllText(path)))
				{
					uint num = (uint)item.Value<long?>("ID").Value;
					if (num != 0)
					{
						_awards.Add(new BossAwardRow(num, (uint)item.Value<long?>("ScoreCount").Value, item["ScoreAward"]));
					}
				}
			}
		}
		catch
		{
		}
		return _awards;
	}

	public static uint ScoreSid(uint levelId)
	{
		return 10 * levelId + 100 + 4;
	}

	public static uint TotalIntegral(PlayerInstance player)
	{
		uint num = 0u;
		foreach (PlayerStrAttr allString in player.Attributes.AllStrings)
		{
			if (allString.Gid == 50 && allString.Sid >= 104 && (allString.Sid - 104) % 10 == 0 && uint.TryParse(allString.Val, out var result))
			{
				num += result;
			}
		}
		return num;
	}

	public static bool IsMileageReceived(PlayerInstance player, uint mileage)
	{
		return !string.IsNullOrEmpty(player.Attributes.GetStringValue(50u, mileage));
	}

	public static void MarkMileageReceived(PlayerInstance player, uint mileage, NtfSyncPlayer sync)
	{
		PlayerStrAttr attr = player.Attributes.SetString(50u, mileage, "1");
		player.Attributes.SyncTo(sync, attr);
	}

	public static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
