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

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_QuickQuestAward")]
public class Activity_QuickQuestAward : ICallGSHandler
{
	private const uint QuestGid = 7u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		long activityId = 0L;
		List<int> ids = new List<int>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("nId", out var nIdEl) && nIdEl.TryGetInt64(out var nId))
			{
				activityId = nId;
			}
			if (root.TryGetProperty("tbIdList", out var list) && list.ValueKind == JsonValueKind.Array)
			{
				ids.AddRange(from x in list.EnumerateArray()
					select x.TryGetInt32(out var value) ? value : 0 into x
					where x > 0
					select x);
			}
		}
		catch
		{
			await CallGSRouter.SendScript(connection, "Activity_QuickQuestAward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = new JsonArray();
		bool claimedAny = false;
		foreach (int questId in ids.Distinct())
		{
			JsonObject row = FindQuest(questId);
			if (row == null)
			{
				continue;
			}
			JsonNode jsonNode = row["Rewards"];
			JsonArray rewardRows = jsonNode as JsonArray;
			if (rewardRows == null || rewardRows.Count == 0)
			{
				continue;
			}
			int need = Int(row, "CheckValue");
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == (uint)questId);
			if (attr == null || ((attr.Val >> 1) & 0x7FFFFFFF) < (uint)need || (attr.Val & 1) != 0)
			{
				continue;
			}
			foreach (JsonNode node in rewardRows)
			{
				JsonArray arr = node as JsonArray;
				if (arr != null && arr.Count >= 5)
				{
					List<uint> values = arr.Select(ReadUInt).Take(5).ToList();
					if (values.Count >= 5 && values[0] != 0)
					{
						await IBLogic_BuyGoods.GrantRewardAsync(player, sync, values);
						rewards.Add(arr.DeepClone());
					}
				}
			}
			attr.Val |= 1u;
			sync.Custom[player.ToPackedAttrKey(7u, (uint)questId)] = attr.Val;
			sync.Custom[player.ToShiftedAttrKey(7u, (uint)questId)] = attr.Val;
			claimedAny = true;
		}
		if (claimedAny)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		JsonObject rsp = new JsonObject
		{
			["nId"] = activityId,
			["tbRewards"] = rewards
		};
		await CallGSRouter.SendScript(connection, "Activity_QuickQuestAward", rsp.ToJsonString(), sync);
	}

	private static JsonObject? FindQuest(int id)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return null;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			return (jsonArray != null) ? jsonArray.OfType<JsonObject>().FirstOrDefault((JsonObject x) => Int(x, "ID") == id) : null;
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

	private static uint ReadUInt(JsonNode? node)
	{
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
