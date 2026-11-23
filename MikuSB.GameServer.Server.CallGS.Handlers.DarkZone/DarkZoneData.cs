using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

internal static class DarkZoneData
{
	public static readonly uint TaskGid = 2u;

	public static readonly (int TaskId, int Section)[] SectionTasks = new (int, int)[4]
	{
		(10508, 2),
		(10608, 4),
		(10709, 7),
		(11110, 10)
	};

	private static JsonArray? _acts;

	private static JsonArray? _gains;

	public static JsonObject? Act()
	{
		if (_acts == null)
		{
			_acts = Load("activity/darkzone.json");
		}
		JsonArray? acts = _acts;
		return (acts != null) ? acts.OfType<JsonObject>().FirstOrDefault() : null;
	}

	public static List<uint> Levels()
	{
		JsonObject jsonObject = Act();
		List<uint> list = new List<uint>();
		if (jsonObject?["Levels"] is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				if (uint.TryParse(item?.ToString(), out var result))
				{
					list.Add(result);
				}
			}
		}
		return list;
	}

	public static JsonArray Gains()
	{
		if (_gains == null)
		{
			_gains = Load("activity/gain.json");
		}
		return _gains ?? new JsonArray();
	}

	public static JsonObject? FindGain(int id)
	{
		return Gains().OfType<JsonObject>().FirstOrDefault((JsonObject x) => ReadInt(x["ID"]) == id);
	}

	private static JsonArray? Load(string rel)
	{
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "darkzone";
			buffer[4] = rel.Replace('/', Path.DirectorySeparatorChar);
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return null;
			}
			return JsonNode.Parse(File.ReadAllText(path))?.AsArray();
		}
		catch
		{
			return null;
		}
	}

	public static int ReadInt(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}

	public static int SectionOf(uint levelId)
	{
		List<uint> list = Levels();
		int num = list.IndexOf(levelId);
		return (num >= 0) ? (num + 1) : 0;
	}

	public static void MarkSection(PlayerInstance player, int section, NtfSyncPlayer sync)
	{
		if (section <= 0)
		{
			return;
		}
		(int, int)[] sectionTasks = SectionTasks;
		for (int i = 0; i < sectionTasks.Length; i++)
		{
			var (taskId, num) = sectionTasks[i];
			if (num == section)
			{
				PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == TaskGid && x.Sid == (uint)taskId);
				if (playerAttr == null)
				{
					playerAttr = new PlayerAttr
					{
						Gid = TaskGid,
						Sid = (uint)taskId,
						Val = 0u
					};
					player.Data.Attrs.Add(playerAttr);
				}
				playerAttr.Val = (playerAttr.Val & 1) | 2;
				sync.Custom[player.ToPackedAttrKey(TaskGid, (uint)taskId)] = playerAttr.Val;
				sync.Custom[player.ToShiftedAttrKey(TaskGid, (uint)taskId)] = playerAttr.Val;
			}
		}
	}
}
