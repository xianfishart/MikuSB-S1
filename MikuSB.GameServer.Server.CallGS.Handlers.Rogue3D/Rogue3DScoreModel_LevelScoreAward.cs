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
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3DScoreModel_LevelScoreAward")]
public class Rogue3DScoreModel_LevelScoreAward : ICallGSHandler
{
	private const uint GroupId = 124u;

	private const uint LevelScoreStart = 2401u;

	private const uint ScoreAwardStart = 2421u;

	private static JsonArray? _rewards;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		int battleId = 0;
		List<int> ids = new List<int>();
		try
		{
			JsonNode req = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			battleId = (req?["nLevelId"]?.GetValue<int>()).GetValueOrDefault();
			JsonNode jsonNode = req?["tbIdList"];
			if (jsonNode is JsonArray list)
			{
				foreach (JsonNode item in list)
				{
					ids.Add(item?.GetValue<int>() ?? 0);
				}
			}
		}
		catch
		{
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray awards = new JsonArray();
		if (player != null && battleId > 0 && ids.Count > 0)
		{
			uint bestScore = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == (uint)(2401 + battleId - 1))?.Val ?? 0;
			uint flagSid = (uint)(2421 + battleId - 1);
			PlayerAttr flagAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 124 && x.Sid == flagSid);
			if (flagAttr == null)
			{
				flagAttr = new PlayerAttr
				{
					Gid = 124u,
					Sid = flagSid,
					Val = 0u
				};
				player.Data.Attrs.Add(flagAttr);
			}
			foreach (int id in ids)
			{
				JsonObject row = FindReward(battleId, id);
				if (row == null || bestScore < ReadUInt(row["TargetScore"]))
				{
					continue;
				}
				uint pos = (uint)(id % 30);
				if (((flagAttr.Val >> (int)pos) & 1) != 0)
				{
					continue;
				}
				flagAttr.Val |= (uint)(1 << (int)pos);
				JsonNode jsonNode = row["Reward"];
				if (!(jsonNode is JsonArray rewardRows))
				{
					continue;
				}
				foreach (JsonNode rewardNode in rewardRows)
				{
					if (!(rewardNode is JsonArray reward))
					{
						continue;
					}
					List<uint> list2 = (from x in reward.Select(ReadUInt)
						where x != 0
						select x).ToList();
					if (list2.Count == 0)
					{
						continue;
					}
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, list2);
					JsonArray echo = new JsonArray();
					foreach (uint value in list2)
					{
						echo.Add(value);
					}
					awards.Add(echo);
				}
			}
			sync.Custom[player.ToPackedAttrKey(124u, flagSid)] = flagAttr.Val;
			sync.Custom[player.ToShiftedAttrKey(124u, flagSid)] = flagAttr.Val;
			DatabaseHelper.SaveDatabaseType(player.Data);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		}
		JsonObject rsp = new JsonObject
		{
			["nLevelId"] = battleId,
			["tbAward"] = awards,
			["tbAwardList"] = awards.DeepClone(),
			["bSuccess"] = true
		};
		await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_LevelScoreAward", rsp.ToJsonString(), sync);
	}

	private static JsonObject? FindReward(int battleId, int id)
	{
		if (_rewards == null)
		{
			_rewards = new JsonArray();
			try
			{
				InlineArray6<string> buffer = default;
				buffer[0] = Directory.GetCurrentDirectory();
				buffer[1] = "Resources";
				buffer[2] = "dlc";
				buffer[3] = "rogue3d";
				buffer[4] = "score_model";
				buffer[5] = "score_reward.json";
				string path = Path.Combine(buffer);
				if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray jsonArray)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject)
						{
							_rewards.Add(jsonObject.DeepClone());
						}
					}
				}
			}
			catch
			{
			}
		}
		foreach (JsonNode reward in _rewards)
		{
			if (!(reward is JsonObject jsonObject2) || ReadUInt(jsonObject2["ScoreBattle"]) != (uint)battleId || ReadUInt(jsonObject2["ID"]) != (uint)id)
			{
				continue;
			}
			return jsonObject2;
		}
		return null;
	}

	private static uint ReadUInt(JsonNode? node)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<double>(out var value))
			{
				return (uint)Math.Max(0.0, value);
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return (uint)Math.Max(0, value2);
			}
			if (jsonValue.TryGetValue<long>(out var value3))
			{
				return (uint)Math.Max(0L, value3);
			}
		}
		return 0u;
	}
}
