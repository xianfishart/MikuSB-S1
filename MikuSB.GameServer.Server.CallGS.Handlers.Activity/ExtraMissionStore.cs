using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

internal static class ExtraMissionStore
{
	public const uint FlagGid = 60u;

	public const uint DiyGid = 50u;

	public const uint DiyStep = 10u;

	public const int TaskCount = 7;

	public const int SumAwardBit = 31;

	private static Dictionary<uint, JsonObject>? _activities;

	public static JsonObject? Activity(uint activityId)
	{
		if (_activities == null)
		{
			_activities = new Dictionary<uint, JsonObject>();
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "activity", "activities.json");
				if (File.Exists(path))
				{
					foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
					{
						if (item is JsonObject jsonObject)
						{
							uint num = ReadUint(jsonObject["Id"]);
							if (num != 0)
							{
								_activities[num] = jsonObject;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _activities.GetValueOrDefault(activityId);
	}

	public static uint DiySid(uint activityId, int taskIdx)
	{
		return (uint)((int)(activityId * 10) + taskIdx + 2);
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

	public static void SetFlag(PlayerInstance player, uint activityId, int bit, NtfSyncPlayer sync)
	{
		PlayerAttr orCreate = player.Attributes.GetOrCreate(60u, activityId);
		orCreate.Val |= (uint)(1 << bit);
		player.Attributes.SyncTo(sync, orCreate);
	}

	public static bool HasFlag(PlayerInstance player, uint activityId, int bit)
	{
		return ((player.GetAttr(60u, activityId) >> bit) & 1) != 0;
	}
}
