using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

[CallGSApi("AchievementDLC_GetReward")]
public class AchievementDLC_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		int nId = ParseInt(param, "nId");
		if (nId <= 0)
		{
			await CallGSRouter.SendScript(connection, "AchievementDLC_GetReward", "{\"tbRewards\":[]}");
			return;
		}
		JsonObject row = DlcMissionStore.Find(nId);
		if (row == null)
		{
			await CallGSRouter.SendScript(connection, "AchievementDLC_GetReward", "{\"tbRewards\":[]}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = GetOrCreate(player, nId);
		if ((attr.Val & 1) != 0)
		{
			await CallGSRouter.SendScript(connection, "AchievementDLC_GetReward", "{\"tbRewards\":[]}");
			return;
		}
		int check = DlcMissionStore.ReadInt(row, "CheckValue");
		uint progress = (attr.Val >> 1) & 0x7FFFFFFF;
		if (progress < check)
		{
			await CallGSRouter.SendScript(connection, "AchievementDLC_GetReward", "{\"sErr\":\"achievement.NotFinished\"}");
			return;
		}
		JsonArray rewards = new JsonArray();
		await GrantRewards(player, sync, row, rewards);
		attr.Val |= 1u;
		SyncAttr(player, sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject response = new JsonObject { ["tbRewards"] = rewards };
		await CallGSRouter.SendScript(connection, "AchievementDLC_GetReward", response.ToJsonString(), sync);
	}

	private static async Task GrantRewards(PlayerInstance player, NtfSyncPlayer sync, JsonObject row, JsonArray output)
	{
		JsonNode jsonNode = row["Rewards"];
		if (!(jsonNode is JsonArray rewards))
		{
			return;
		}
		foreach (JsonNode node in rewards)
		{
			JsonArray arr = node as JsonArray;
			if (arr != null && arr.Count >= 5)
			{
				List<uint> reward = arr.Select((JsonNode x) => (uint)DlcMissionStore.ReadIntNode(x)).Take(5).ToList();
				if (reward.Count >= 5 && reward[0] != 0)
				{
					await VirCapture_GetLevelAward.GrantRewardAsync(player, sync, reward);
					output.Add(arr.DeepClone());
				}
			}
		}
	}

	private static PlayerAttr GetOrCreate(PlayerInstance player, int missionId)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 14 && x.Sid == (uint)missionId);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = 14u,
			Sid = (uint)missionId,
			Val = 0u
		};
		player.Data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	private static void SyncAttr(PlayerInstance player, NtfSyncPlayer sync, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}

	private static int ParseInt(string param, string key)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			JsonElement value;
			return jsonDocument.RootElement.TryGetProperty(key, out value) ? value.GetInt32() : 0;
		}
		catch
		{
			return 0;
		}
	}
}
