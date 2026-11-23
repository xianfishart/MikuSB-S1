using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("DLC_Chapter_STAR_AWARD")]
public class DLC_Chapter_STAR_AWARD : ICallGSHandler
{
	private const uint LevelStarGroup = 21u;

	private const uint StarClaimGroup = 30u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		try
		{
			File.AppendAllText("D:\\DSH\\star_debug.log", "REQ " + param + "\n");
		}
		catch
		{
		}
		List<(int, int)> requests = ParseRequests(param);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = new JsonArray();
		foreach (var item in requests)
		{
			int chapterId = item.Item1;
			int index = item.Item2;
			JsonObject chapter = LoadChapter(chapterId);
			if (chapter == null)
			{
				continue;
			}
			JsonArray obj2 = chapter["Level"] as JsonArray;
			List<int> levels = ((obj2 != null) ? (from x in obj2
				select x?.GetValue<int>() ?? 0 into x
				where x > 0
				select x).ToList() : null) ?? new List<int>();
			if (!(chapter["StarAward"] is JsonObject starAward))
			{
				continue;
			}
			List<int> thresholds = (from kv in starAward
				select int.TryParse(kv.Key, out var result) ? result : 0 into v
				where v > 0
				orderby v
				select v).ToList();
			if (index <= 0 || index > thresholds.Count)
			{
				continue;
			}
			int threshold = thresholds[index - 1];
			PlayerAttr mask = GetOrCreate(player, 30u, (uint)chapterId);
			if ((mask.Val & (uint)(1 << index)) != 0 || CountStars(player, levels) < threshold)
			{
				continue;
			}
			JsonNode jsonNode = starAward[threshold.ToString()];
			if (!(jsonNode is JsonArray awardRows))
			{
				continue;
			}
			foreach (JsonNode node in awardRows)
			{
				JsonArray row = node as JsonArray;
				if (row != null && row.Count >= 5)
				{
					List<uint> reward = row.Select((JsonNode x) => (uint)ReadInt(x)).Take(5).ToList();
					if (reward.Count >= 5 && reward[0] != 0)
					{
						await VirCapture_GetLevelAward.GrantRewardAsync(player, sync, reward);
						rewards.Add(row.DeepClone());
					}
				}
			}
			mask.Val |= (uint)(1 << index);
			sync.Custom[player.ToPackedAttrKey(30u, (uint)chapterId)] = mask.Val;
			sync.Custom[player.ToShiftedAttrKey(30u, (uint)chapterId)] = mask.Val;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		await CallGSRouter.SendScript(connection, "DLC_Chapter_STAR_AWARD", rewards.ToJsonString(), sync);
	}

	private static int CountStars(PlayerInstance player, List<int> levels)
	{
		int num = 0;
		foreach (int level in levels)
		{
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == (uint)level);
			if (playerAttr != null)
			{
				num += BitOperations.PopCount(playerAttr.Val & 7);
			}
		}
		return num;
	}

	private static JsonObject? LoadChapter(int chapterId)
	{
		try
		{
			InlineArray6<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "dlc1";
			buffer[4] = "chapter";
			buffer[5] = "chapter.json";
			string path = Path.Combine(buffer);
			return (JsonNode.Parse(File.ReadAllText(path)) is JsonArray source) ? source.OfType<JsonObject>().FirstOrDefault((JsonObject x) => ReadInt(x["ID"]) == chapterId) : null;
		}
		catch
		{
			return null;
		}
	}

	private static List<(int, int)> ParseRequests(string param)
	{
		List<(int, int)> list = new List<(int, int)>();
		try
		{
			JsonNode jsonNode = JsonNode.Parse(param);
			if (!(jsonNode is JsonArray jsonArray))
			{
				return list;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonArray { Count: >=2 } jsonArray2)
				{
					list.Add((ReadInt(jsonArray2[0]), ReadInt(jsonArray2[1])));
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static PlayerAttr GetOrCreate(PlayerInstance player, uint gid, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid,
			Val = 0u
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static int ReadInt(JsonNode? node)
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
		}
		return 0;
	}
}
