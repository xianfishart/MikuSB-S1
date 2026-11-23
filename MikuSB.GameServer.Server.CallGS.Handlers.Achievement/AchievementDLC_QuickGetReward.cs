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

[CallGSApi("AchievementDLC_QuickGetReward")]
public class AchievementDLC_QuickGetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<int> ids = new List<int>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(param);
			if (doc.RootElement.TryGetProperty("tbIdList", out var list) && list.ValueKind == JsonValueKind.Array)
			{
				ids.AddRange(from x in list.EnumerateArray()
					select x.TryGetInt32(out var value) ? value : 0 into x
					where x > 0
					select x);
			}
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray all = new JsonArray();
		foreach (int missionId in ids.Distinct())
		{
			JsonObject row = DlcMissionStore.Find(missionId);
			if (row == null)
			{
				continue;
			}
			PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 14 && x.Sid == (uint)missionId);
			if (attr != null && (attr.Val & 1) == 0)
			{
				int check = DlcMissionStore.ReadInt(row, "CheckValue");
				if (((attr.Val >> 1) & 0x7FFFFFFF) >= check)
				{
					JsonArray one = new JsonArray();
					await AchievementDLC_GetReward_Grant(player, sync, row, one);
					attr.Val |= 1u;
					sync.Custom[player.ToPackedAttrKey(14u, (uint)missionId)] = attr.Val;
					sync.Custom[player.ToShiftedAttrKey(14u, (uint)missionId)] = attr.Val;
					all.Add(one);
				}
			}
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		await CallGSRouter.SendScript(connection, "AchievementDLC_QuickGetReward", new JsonObject { ["tbRewards"] = all }.ToJsonString(), sync);
	}

	private static async Task AchievementDLC_GetReward_Grant(PlayerInstance player, NtfSyncPlayer sync, JsonObject row, JsonArray output)
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
}
