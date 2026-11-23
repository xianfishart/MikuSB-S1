using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.SeasonBoss;

internal static class SeasonBossQuest
{
	private sealed record SeasonRow(uint[] Levels, uint[] Tasks);

	private static List<SeasonRow>? _rows;

	private static List<SeasonRow> Rows()
	{
		if (_rows != null)
		{
			return _rows;
		}
		_rows = new List<SeasonRow>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "seasonboss";
			buffer[4] = "seasonlist.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return _rows;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (item is JsonObject jsonObject)
				{
					uint[] array = ReadList(jsonObject["LevelList"]);
					uint[] array2 = ReadList(jsonObject["TaskID"]);
					if (array.Length != 0 && array2.Length != 0)
					{
						_rows.Add(new SeasonRow(array, array2));
					}
				}
			}
		}
		catch
		{
		}
		return _rows;
	}

	public static bool IsSeasonBossTask(uint taskId)
	{
		if (taskId == 0)
		{
			return false;
		}
		return Rows().Any((SeasonRow r) => r.Tasks.Contains(taskId));
	}

	public static void Complete(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
	{
		if (levelId == 0)
		{
			return;
		}
		foreach (SeasonRow item in Rows())
		{
			int num = Array.IndexOf(item.Levels, levelId);
			if (num >= 0 && num < item.Tasks.Length)
			{
				uint num2 = item.Tasks[num];
				if (GameData.ActivityQuestData.TryGetValue(num2, out ActivityQuestExcel value))
				{
					AchievementClaimService.Complete(player, 7u, num2, value.CheckValue, sync);
					break;
				}
			}
		}
	}

	private static uint[] ReadList(JsonNode? node)
	{
		if (!(node is JsonArray jsonArray))
		{
			return Array.Empty<uint>();
		}
		List<uint> list = new List<uint>();
		foreach (JsonNode item in jsonArray)
		{
			if (item is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value) && value != 0)
			{
				list.Add(value);
			}
		}
		return list.ToArray();
	}
}
