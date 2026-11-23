using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_GetStarAward")]
public class Chapter_GetStarAward : ICallGSHandler
{
	private const uint StarAwardGroupId = 20u;

	private const uint LegacyStarAwardGroupId = 201u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		StarAwardParam req = JsonSerializer.Deserialize<StarAwardParam>(param);
		if (req == null || req.ChapterId <= 0)
		{
			await CallGSRouter.SendScript(connection, "Chapter_GetStarAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		JsonNode jsonNode = LoadChapter(req.ChapterId)?["StarAward"];
		if (!(jsonNode is JsonObject starAward))
		{
			await CallGSRouter.SendScript(connection, "Chapter_GetStarAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray responseRewards = new JsonArray();
		JsonArray claimedIndexes = new JsonArray();
		bool claimedAny = false;
		List<int> thresholds = (from kv in starAward
			select int.TryParse(kv.Key, out var result) ? result : 0 into value
			where value > 0
			orderby value
			select value).ToList();
		if (req.Index > thresholds.Count)
		{
			await CallGSRouter.SendScript(connection, "Chapter_GetStarAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint sid = (uint)((req.ChapterId << 8) | req.Difficult);
		PlayerAttr mask = GetOrCreateAttr(player, 20u, sid);
		int starCount = GetChapterStarCount(player, req.ChapterId);
		for (int i = 0; i < thresholds.Count; i++)
		{
			int index = i + 1;
			if (req.Index > 0 && req.Index != index)
			{
				continue;
			}
			int threshold = thresholds[i];
			if (threshold > starCount)
			{
				continue;
			}
			jsonNode = starAward[threshold.ToString()];
			if (!(jsonNode is JsonArray rewards))
			{
				continue;
			}
			uint bit = (uint)(1 << index);
			if ((mask.Val & bit) != 0)
			{
				continue;
			}
			uint legacySid = (uint)(req.ChapterId * 1000 + threshold);
			PlayerAttr legacy = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 201 && x.Sid == legacySid && x.Val != 0);
			if (legacy != null)
			{
				mask.Val |= bit;
				SyncAttr(player, sync, mask);
				claimedIndexes.Add(threshold);
				claimedAny = true;
				continue;
			}
			foreach (JsonNode rewardNode in rewards)
			{
				JsonArray row = rewardNode as JsonArray;
				if (row != null && row.Count >= 5)
				{
					List<uint> reward = row.Select((JsonNode x) => (uint)ReadInt(x)).Take(5).ToList();
					if (reward.Count >= 5 && reward[0] != 0)
					{
						await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
						responseRewards.Add(row.DeepClone());
					}
				}
			}
			mask.Val |= bit;
			SyncAttr(player, sync, mask);
			claimedIndexes.Add(threshold);
			claimedAny = true;
		}
		if (!claimedAny)
		{
			await CallGSRouter.SendScript(connection, "Chapter_GetStarAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject rsp = new JsonObject
		{
			["nChapterID"] = req.ChapterId,
			["nDifficult"] = req.Difficult,
			["bMain"] = req.Main,
			["nIndex"] = req.Index,
			["nAwardIndex"] = req.Index,
			["tbAward"] = responseRewards,
			["tbRewards"] = responseRewards.DeepClone(),
			["tbStarAward"] = responseRewards.DeepClone(),
			["tbGotIndex"] = claimedIndexes,
			["tbIndex"] = claimedIndexes.DeepClone()
		};
		await CallGSRouter.SendScript(connection, "Chapter_GetStarAward", rsp.ToJsonString(), sync);
	}

	private static int GetChapterStarCount(PlayerInstance player, int chapterId)
	{
		if (!(LoadChapter(chapterId)?["Level"] is JsonArray jsonArray))
		{
			return 0;
		}
		int num = 0;
		foreach (JsonNode item in jsonArray)
		{
			uint levelId = (uint)ReadInt(item);
			if (levelId == 0)
			{
				continue;
			}
			PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 21 && x.Sid == levelId);
			if (playerAttr != null)
			{
				for (int num2 = (int)(playerAttr.Val & 7); num2 != 0; num2 >>= 1)
				{
					num += num2 & 1;
				}
			}
		}
		return num;
	}

	private static JsonObject? LoadChapter(int chapterId)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "chapter", "chapter_main.json");
			if (!File.Exists(path))
			{
				return null;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return null;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					JsonNode? jsonNode = jsonObject["ID"];
					if (jsonNode != null && jsonNode.GetValue<int>() == chapterId)
					{
						return jsonObject;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static PlayerAttr GetOrCreateAttr(PlayerInstance player, uint gid, uint sid)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
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
			if (jsonValue.TryGetValue<double>(out var value3))
			{
				return (int)value3;
			}
		}
		return 0;
	}
}
