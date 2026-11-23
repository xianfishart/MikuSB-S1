using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_GetRikiAward")]
public class VirCapture_GetRikiAward : ICallGSHandler
{
	private const uint VirCaptureGroupId = 128u;

	private const uint RikiGroupId = 135u;

	private const uint RikiAwardFlagStartSid = 121u;

	private const uint RikiAwardFlagEndSid = 140u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		VirCaptureGetRikiAwardParam req = JsonSerializer.Deserialize<VirCaptureGetRikiAwardParam>(param);
		if (req == null || req.IdList == null || req.IdList.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "VirCapture_GetRikiAward", "{\"tbAwardList\":[]}");
			return;
		}
		List<JsonObject> rows = LoadRows();
		int currentCount = Enumerable.Count(player.Data.Attrs, (PlayerAttr x) => x.Gid == 135 && x.Val != 0);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray responseAwards = new JsonArray();
		foreach (int raw in from x in req.IdList.Distinct()
			orderby x
			select x)
		{
			uint id = (uint)raw;
			JsonObject row = rows.FirstOrDefault((JsonObject x) => (x["Id"]?.GetValue<int>() ?? 0) == raw || (x["Point"]?.GetValue<int>() ?? 0) == raw);
			if (row == null || currentCount < (row["Point"]?.GetValue<int>() ?? 0) || !CanClaim(player.Data, id))
			{
				continue;
			}
			SetClaimed(player, sync, id);
			JsonNode jsonNode = row["Rewards"];
			if (!(jsonNode is JsonArray rewards))
			{
				continue;
			}
			JsonArray group = new JsonArray();
			foreach (JsonNode rewardNode in rewards)
			{
				JsonArray rewardArray = rewardNode as JsonArray;
				if (rewardArray != null && rewardArray.Count >= 5)
				{
					List<uint> reward = rewardArray.Select((JsonNode x) => (uint)(x?.GetValue<int>() ?? 0)).Take(5).ToList();
					if (reward.Count >= 5 && reward[0] != 0)
					{
						await VirCapture_GetLevelAward.GrantRewardAsync(player, sync, reward);
						InlineArray5<JsonNode> buffer = default;
						buffer[0] = (int)reward[0];
						buffer[1] = (int)reward[1];
						buffer[2] = (int)reward[2];
						buffer[3] = (int)reward[3];
						buffer[4] = (int)reward[4];
						group.Add(new JsonArray(buffer));
					}
				}
			}
			responseAwards.Add(group);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JsonObject rsp = new JsonObject { ["tbAwardList"] = responseAwards };
		await CallGSRouter.SendScript(connection, "VirCapture_GetRikiAward", rsp.ToJsonString(), sync);
	}

	private static bool CanClaim(PlayerGameData data, uint id)
	{
		uint sid = 121 + id / 30;
		if (sid < 121 || sid > 140)
		{
			return false;
		}
		int num = (int)(id % 30);
		return ((data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid)?.Val ?? 0) & (uint)(1 << num)) == 0;
	}

	private static void SetClaimed(PlayerInstance player, NtfSyncPlayer sync, uint id)
	{
		uint sid = 121 + id / 30;
		int num = (int)(id % 30);
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 128 && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = 128u,
				Sid = sid,
				Val = 0u
			};
			player.Data.Attrs.Add(playerAttr);
		}
		playerAttr.Val |= (uint)(1 << num);
		sync.Custom[player.ToPackedAttrKey(playerAttr.Gid, playerAttr.Sid)] = playerAttr.Val;
		sync.Custom[player.ToShiftedAttrKey(playerAttr.Gid, playerAttr.Sid)] = playerAttr.Val;
	}

	private static List<JsonObject> LoadRows()
	{
		List<JsonObject> list = new List<JsonObject>();
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "dlc";
			buffer[3] = "vircapture";
			buffer[4] = "rikiawardlist.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return list;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return list;
			}
			foreach (JsonNode item2 in jsonArray)
			{
				if (item2 is JsonObject item)
				{
					list.Add(item);
				}
			}
		}
		catch
		{
		}
		return list;
	}
}
