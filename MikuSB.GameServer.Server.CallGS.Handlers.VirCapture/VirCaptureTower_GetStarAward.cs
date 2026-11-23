using System;
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
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTower_GetStarAward")]
public class VirCaptureTower_GetStarAward : ICallGSHandler
{
	private sealed record Tier(uint Threshold, JsonArray Rewards);

	private const uint GroupId = 128u;

	private const uint ChapterFlagBase = 140u;

	private const uint StarGroupId = 21u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		int chapterId = ReadInt(request["nChapterId"]);
		int difficult = ReadInt(request["nDifficult"]);
		int index = ReadInt(request["nIndex"]);
		JsonObject chapter = FindChapter(chapterId, difficult);
		if (chapter == null)
		{
			await CallGSRouter.SendScript(connection, "VirCaptureTower_GetStarAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint stars = CountChapterStars(player, chapter);
		List<Tier> tiers = ReadTiers(chapter);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		List<int> claimed = new List<int>();
		for (int i = 0; i < tiers.Count; i++)
		{
			int tierIndex = i + 1;
			if ((index > 0 && tierIndex != index) || stars < tiers[i].Threshold || HasFlag(player, chapterId, tierIndex))
			{
				continue;
			}
			List<List<uint>> rows = ParseRewardRows(tiers[i].Rewards);
			if (rows.Count == 0)
			{
				continue;
			}
			foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
			{
				granted.Add(item?.DeepClone());
			}
			SetFlag(player, chapterId, tierIndex, sync);
			claimed.Add(tierIndex);
		}
		if (claimed.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		await CallGSRouter.SendScript(connection, "VirCaptureTower_GetStarAward", new JsonObject
		{
			["nChapterId"] = chapterId,
			["nDifficult"] = difficult,
			["tbAward"] = granted
		}.ToJsonString(), sync);
	}

	private static JsonObject? FindChapter(int chapterId, int difficult)
	{
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "vircapture";
			buffer[4] = "tower_chapter.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return null;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (!(item is JsonObject jsonObject) || ReadInt(jsonObject["ID"]) != chapterId || ReadInt(jsonObject["Difficult"]) != difficult)
				{
					continue;
				}
				return jsonObject;
			}
		}
		catch
		{
		}
		return null;
	}

	private static List<Tier> ReadTiers(JsonObject chapter)
	{
		List<Tier> list = new List<Tier>();
		if (!(chapter["StarAward"] is JsonObject jsonObject))
		{
			return list;
		}
		foreach (var (s, jsonNode2) in jsonObject)
		{
			if (uint.TryParse(s, out var result) && jsonNode2 is JsonArray rewards)
			{
				list.Add(new Tier(result, rewards));
			}
		}
		list.Sort((Tier a, Tier b) => a.Threshold.CompareTo(b.Threshold));
		return list;
	}

	private static uint CountChapterStars(PlayerInstance player, JsonObject chapter)
	{
		uint num = 0u;
		if (!(chapter["Level"] is JsonArray jsonArray))
		{
			return num;
		}
		foreach (JsonNode item in jsonArray)
		{
			uint num2 = ReadUint(item);
			if (num2 != 0)
			{
				uint value = player.GetAttr(21u, num2) & 0xFF;
				num += (uint)BitOperations.PopCount(value);
			}
		}
		return num;
	}

	private static bool HasFlag(PlayerInstance player, int chapterId, int index)
	{
		uint sid = (uint)((ulong)chapterId + 140uL);
		uint attr = player.GetAttr(128u, sid);
		return ((attr >> index) & 1) != 0;
	}

	private static void SetFlag(PlayerInstance player, int chapterId, int index, NtfSyncPlayer sync)
	{
		uint sid = (uint)((ulong)chapterId + 140uL);
		PlayerAttr orCreate = player.Attributes.GetOrCreate(128u, sid);
		orCreate.Val |= (uint)(1 << index);
		player.Attributes.SyncTo(sync, orCreate);
	}

	private static List<List<uint>> ParseRewardRows(JsonArray rows)
	{
		List<List<uint>> list = new List<List<uint>>();
		foreach (JsonNode row in rows)
		{
			string text = row?.ToString() ?? "";
			string[] array = text.Trim().Trim(new char[3] { '[', ']', '"' }).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (array.Length < 5)
			{
				continue;
			}
			List<uint> list2 = new List<uint>(5);
			bool flag = true;
			foreach (string item in array.Take(5))
			{
				if (!uint.TryParse(item, out var result))
				{
					flag = false;
					break;
				}
				list2.Add(result);
			}
			if (flag && list2[4] != 0)
			{
				list.Add(list2);
			}
		}
		return list;
	}

	private static int ReadInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
		{
			return value;
		}
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
