using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

internal static class ActivityQuestProgress
{
	private const uint QuestGid = 7u;

	private static JsonArray? _rows;

	public static void Ensure(PlayerInstance player)
	{
		try
		{
			JsonArray jsonArray = LoadRows();
			if (jsonArray == null)
			{
				return;
			}
			bool flag = false;
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || Int(jsonObject, "Type") != 350)
				{
					continue;
				}
				int id = Int(jsonObject, "ID");
				if (id <= 0)
				{
					continue;
				}
				int num = Int(jsonObject, "CheckValue");
				if (num <= 0)
				{
					num = 1;
				}
				uint num2 = LastNumber(jsonObject["Param"]);
				if (num2 != 0 && (player.GetAttr(22u, num2) != 0 || player.GetAttr(21u, num2) != 0))
				{
					PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == (uint)id);
					if (playerAttr == null)
					{
						playerAttr = new PlayerAttr
						{
							Gid = 7u,
							Sid = (uint)id,
							Val = 0u
						};
						player.Data.Attrs.Add(playerAttr);
					}
					uint num3 = playerAttr.Val & 1;
					uint num4 = (playerAttr.Val >> 1) & 0x7FFFFFFF;
					if (num3 == 0 && num4 < (uint)num)
					{
						playerAttr.Val = num3 | (uint)(num << 1);
						flag = true;
					}
				}
			}
			if (flag)
			{
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
	}

	private static JsonArray? LoadRows()
	{
		if (_rows != null)
		{
			return _rows;
		}
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return null;
			}
			_rows = JsonNode.Parse(File.ReadAllText(path)) as JsonArray;
			return _rows;
		}
		catch
		{
			return null;
		}
	}

	private static int Int(JsonObject row, string key)
	{
		int result;
		return int.TryParse(row[key]?.ToString(), out result) ? result : 0;
	}

	private static uint LastNumber(JsonNode? node)
	{
		if (node == null)
		{
			return 0u;
		}
		MatchCollection matchCollection = Regex.Matches(node.ToJsonString(), "\\d+");
		if (matchCollection.Count == 0)
		{
			return 0u;
		}
		uint result;
		return uint.TryParse(matchCollection[matchCollection.Count - 1].Value, out result) ? result : 0u;
	}
}
