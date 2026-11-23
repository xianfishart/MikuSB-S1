using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

internal static class DlcMissionStore
{
	private static List<JsonObject>? _rows;

	public static JsonObject? Find(int missionId)
	{
		if (_rows == null)
		{
			_rows = Load();
		}
		return _rows.FirstOrDefault((JsonObject x) => ReadInt(x, "MissionID") == missionId);
	}

	public static IEnumerable<JsonObject> ActiveRows()
	{
		if (_rows == null)
		{
			_rows = Load();
		}
		return _rows;
	}

	public static int ReadInt(JsonObject row, string key)
	{
		JsonNode jsonNode;
		return row.TryGetPropertyValue(key, out jsonNode) ? ReadIntNode(jsonNode) : 0;
	}

	public static int ReadIntNode(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				return value;
			}
			if (jsonValue.TryGetValue<long>(out var value2))
			{
				return (int)value2;
			}
			if (jsonValue.TryGetValue<double>(out var value3))
			{
				return (int)value3;
			}
			if (jsonValue.TryGetValue<string>(out string value4) && int.TryParse(value4, out var result))
			{
				return result;
			}
		}
		return 0;
	}

	private static List<JsonObject> Load()
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_mission.json");
			if (!File.Exists(path))
			{
				return new List<JsonObject>();
			}
			return ((JsonNode.Parse(File.ReadAllText(path)) is JsonArray source) ? source.OfType<JsonObject>().ToList() : null) ?? new List<JsonObject>();
		}
		catch
		{
			return new List<JsonObject>();
		}
	}
}
