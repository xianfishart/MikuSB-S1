using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

[CallGSApi("DefendLogic_GetReward")]
public class DefendLogic_GetReward : ICallGSHandler
{
	private sealed record TargetRow(uint Id, uint Wave, JsonArray Awards);

	private static List<TargetRow>? _targets;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint actId = DefendStore.ReadUint(request["nID"]);
		uint diff = DefendStore.ReadUint(request["nDiff"]);
		uint targetId = DefendStore.ReadUint(request["nTargetId"]);
		bool all = request["bAll"]?.GetValue<bool>() ?? false;
		uint levelId = FindActLevel(actId);
		JsonObject level = ((levelId == 0) ? null : FindLevel(levelId, diff));
		JsonArray targets = default;
		int num;
		if (level != null)
		{
			JsonNode jsonNode = level["Target"];
			targets = jsonNode as JsonArray;
			num = ((targets == null) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		if (num != 0)
		{
			await CallGSRouter.SendScript(connection, "DefendLogic_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint passWave = player.GetAttr(10u, 2u);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray granted = new JsonArray();
		for (int i = 0; i < targets.Count; i++)
		{
			uint id = DefendStore.ReadUint(targets[i]);
			if (id == 0 || (!all && targetId != 0 && id != targetId))
			{
				continue;
			}
			TargetRow target = FindTarget(id);
			if (target == null || passWave < target.Wave)
			{
				continue;
			}
			int index = i + 1;
			uint flagSid = ((index <= 30) ? 3u : 4u);
			int bit = ((index <= 30) ? index : (index - 30));
			PlayerAttr flag = DefendStore.GetOrCreate(player, flagSid);
			if (((flag.Val >> bit) & 1) != 0)
			{
				continue;
			}
			List<List<uint>> rows = ParseRewardRows(target.Awards);
			if (rows.Count == 0)
			{
				continue;
			}
			foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
			{
				granted.Add(item?.DeepClone());
			}
			flag.Val |= (uint)(1 << bit);
			player.Attributes.SyncTo(sync, flag);
			if (!all)
			{
				break;
			}
		}
		if (granted.Count > 0)
		{
			SavePlayer(player);
		}
		await CallGSRouter.SendScript(connection, "DefendLogic_GetReward", new JsonObject
		{
			["tbReward"] = granted,
			["bGetAll"] = all
		}.ToJsonString(), sync);
	}

	private static TargetRow? FindTarget(uint targetId)
	{
		if (_targets == null)
		{
			_targets = new List<TargetRow>();
			try
			{
				InlineArray5<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "challenge";
				buffer[3] = "defend";
				buffer[4] = "level_target.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path))
				{
					foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
					{
						if (item is JsonObject jsonObject)
						{
							uint num = DefendStore.ReadUint(jsonObject["Id"]);
							if (num != 0 && jsonObject["Award"] is JsonArray awards)
							{
								_targets.Add(new TargetRow(num, DefendStore.ReadUint(jsonObject["Param"]), awards));
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		return _targets.FirstOrDefault((TargetRow x) => x.Id == targetId);
	}

	private static uint FindActLevel(uint actId)
	{
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "defend";
			buffer[4] = "defend_act.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return 0u;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (!(item is JsonObject jsonObject) || DefendStore.ReadUint(jsonObject["ID"]) != actId)
				{
					continue;
				}
				return DefendStore.ReadUint(jsonObject["LevelId"]);
			}
		}
		catch
		{
		}
		return 0u;
	}

	private static JsonObject? FindLevel(uint levelId, uint diff)
	{
		JsonObject jsonObject = null;
		try
		{
			InlineArray5<string> buffer = default;
			buffer[0] = Directory.GetCurrentDirectory();
			buffer[1] = "Resources";
			buffer[2] = "challenge";
			buffer[3] = "defend";
			buffer[4] = "level.json";
			string path = Path.Combine(buffer);
			if (!File.Exists(path))
			{
				return null;
			}
			foreach (JsonNode item in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (item is JsonObject jsonObject2 && DefendStore.ReadUint(jsonObject2["LevelId"]) == levelId)
				{
					if (DefendStore.ReadUint(jsonObject2["Diff"]) == diff)
					{
						return jsonObject2;
					}
					if (jsonObject == null)
					{
						jsonObject = jsonObject2;
					}
				}
			}
		}
		catch
		{
		}
		return jsonObject;
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

	private static void SavePlayer(PlayerInstance player)
	{
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
	}
}
