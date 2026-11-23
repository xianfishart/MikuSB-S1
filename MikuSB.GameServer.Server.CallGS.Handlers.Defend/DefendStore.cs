using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

internal static class DefendStore
{
	public sealed record LevelAwardRow(uint ActId, uint Index, uint NeedExp, List<uint> Award);

	public const uint Gid = 10u;

	public const uint ActIdSid = 0u;

	public const uint MaxWaveSid = 2u;

	public const uint ScoreSid = 5u;

	public const uint MaxSingleScoreSid = 7u;

	public const uint DiffSid = 8u;

	public const uint AwardFlagStart = 20u;

	public const uint AwardFlagEnd = 30u;

	private static List<LevelAwardRow>? _rows;

	public static List<LevelAwardRow> LevelAwards()
	{
		if (_rows != null)
		{
			return _rows;
		}
		_rows = new List<LevelAwardRow>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "defend";
			buffer[4] = "levelaward_list.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return _rows;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (!(item is JsonObject jsonObject))
				{
					continue;
				}
				uint num = ReadUint(jsonObject["ActID"]);
				uint num2 = ReadUint(jsonObject["Index"]);
				if (num == 0 || num2 == 0)
				{
					continue;
				}
				List<uint> list = new List<uint>();
				if (jsonObject["AwardList"] is JsonArray jsonArray)
				{
					foreach (JsonNode item2 in jsonArray)
					{
						list.Add(ReadUint(item2));
					}
				}
				_rows.Add(new LevelAwardRow(num, num2, ReadUint(jsonObject["NeedExp"]), list));
			}
		}
		catch
		{
		}
		return _rows;
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

	public static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(10u, sid);
		orCreate.Val = value;
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static PlayerAttr GetOrCreate(PlayerInstance player, uint sid)
	{
		return player.Attributes.GetOrCreate(10u, sid);
	}
}
