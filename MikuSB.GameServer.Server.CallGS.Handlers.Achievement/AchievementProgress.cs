using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

internal static class AchievementProgress
{
	public static void MarkLevel(PlayerInstance player, uint levelId, NtfSyncPlayer sync)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (!(item is JsonObject jsonObject) || !int.TryParse(jsonObject["Type"]?.ToString(), out var result) || result != 350 || !(jsonObject["Param"] is JsonValue jsonValue))
				{
					continue;
				}
				uint num = ParseFirstLevel(jsonValue.ToString());
				if (num == levelId && int.TryParse(jsonObject["ID"]?.ToString(), out var id) && id > 0)
				{
					PlayerAttr playerAttr = player.Attributes.GetOrCreate(7u, (uint)id);
					playerAttr.Val |= 2u;
					sync.Custom[player.ToPackedAttrKey(7u, (uint)id)] = playerAttr.Val;
					sync.Custom[player.ToShiftedAttrKey(7u, (uint)id)] = playerAttr.Val;
				}
			}
		}
		catch
		{
		}
	}

	private static uint ParseFirstLevel(string param)
	{
		int num = param.IndexOf(',');
		if (num < 0)
		{
			return 0u;
		}
		int num2 = param.IndexOf('{', num + 1);
		if (num2 < 0)
		{
			return 0u;
		}
		int num3 = param.IndexOf('}', num2 + 1);
		if (num3 < 0)
		{
			return 0u;
		}
		int num4 = num2 + 1;
		uint result;
		return uint.TryParse(param.Substring(num4, num3 - num4), out result) ? result : 0u;
	}
}
