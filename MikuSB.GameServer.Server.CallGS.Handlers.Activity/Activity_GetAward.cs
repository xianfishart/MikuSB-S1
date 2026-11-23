using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Shop;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_GetAward")]
public class Activity_GetAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JObject json = JObject.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
		MikuSB.Util.Logger.GetByClassName().Info($"[GETAWARD] uid={connection.Player?.Uid} param={param}");
		long nId = json.Value<long?>("nId").GetValueOrDefault();
		long nType = json.Value<long?>("nType").GetValueOrDefault();
		long questId = json.Value<long?>("nQuestId").GetValueOrDefault();
		if (nId == 20)
		{
			await HandleDiySsr(connection, nId, json);
			return;
		}
		if (questId > 0)
		{
			await HandleQuestReward(connection, nId, questId);
			return;
		}
		// 第 432 节：感知巡游（energy_exchange）的兑换格。客户端点格子时发的是
		// {"nID":<awardId>,"nId":<活动id>}，nID 就是 graph.json 里那一格的奖励 id。
		long exchangeAward = ReadProperty(json, "nID");
		if (exchangeAward > 0 && MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.IsEnergyActivity((uint)nId))
		{
			await HandleEnergyExchange(connection, (uint)nId, (uint)exchangeAward);
			return;
		}
		if (nType == 1)
		{
			// 第 432 节：每日签到真正发奖。原来只回一条空 tbRewards 的 DaySign，
			// 既不写「今日已签」也不发东西，客户端就一直停在可签状态。
			PlayerInstance player = connection.Player;
			if (player == null)
			{
				return;
			}
			NtfSyncPlayer daySync = new NtfSyncPlayer();
			System.Text.Json.Nodes.JsonArray rewarded = await MikuSB.GameServer.Game.Activity.ActivityDaySignService.SignAsync(player, (uint)nId, daySync);
			System.Text.Json.Nodes.JsonObject payload = new System.Text.Json.Nodes.JsonObject
			{
				["tbRewards"] = rewarded ?? new System.Text.Json.Nodes.JsonArray()
			};
			await CallGSRouter.SendScript(connection, "DaySign", payload.ToJsonString(), daySync);
			return;
		}
		await CallGSRouter.SendScript(connection, "ShortSign", $"{{\"nId\":{nId}}}");
	}

	private static async Task HandleDiySsr(Connection connection, long activityId, JObject json)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 50 && x.Sid == 203);
		if (attr != null && attr.Val != 0)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"tip.ssr.Selected\"}");
			return;
		}
		JArray item = json["tbItem"] as JArray;
		if (item == null || item.Count < 4)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		List<uint> reward = (from x in item.Take(4).Select(ReadInt)
			select (uint)x).ToList();
		reward.Add(1u);
		JToken jToken = LoadActivity(activityId)?["CustomData"];
		JArray allowed = ((jToken is JArray { Count: >1 } custom) ? (custom[1] as JArray) : null);
		if (allowed == null || !allowed.OfType<JArray>().Any((JArray x) => x.Count >= 4 && x.Take(4).Select(ReadInt).SequenceEqual(from result in reward.Take(4)
			select (int)result)))
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = 50u,
				Sid = 203u,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		attr.Val = 1u;
		sync.Custom[player.ToPackedAttrKey(50u, 203u)] = 1u;
		sync.Custom[player.ToShiftedAttrKey(50u, 203u)] = 1u;
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JObject response = new JObject
		{
			["nId"] = activityId,
			["tbRewards"] = new JArray
			{
				new JArray(((IEnumerable<uint>)reward).Select((Func<uint, long>)((uint x) => x)))
			}
		};
		await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", response.ToString(), sync);
	}

	private static long ReadProperty(JObject json, string name)
	{
		foreach (JProperty prop in json.Properties())
		{
			if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				if (prop.Value.Type == JTokenType.Integer)
				{
					return prop.Value.Value<long>();
				}
				long parsed;
				return long.TryParse(prop.Value.ToString(), out parsed) ? parsed : 0L;
			}
		}
		return 0L;
	}

	/// <summary>
	/// 感知巡游兑换（第 432 节）。巡游度存 50/(活动id*10+3)，领取标记存字符串属性 54/1 的 JSON。
	/// </summary>
	private static async Task HandleEnergyExchange(Connection connection, uint activityId, uint awardId)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.ExchangeRow row = MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.Find(awardId);
		if (row == null)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		JObject flags = LoadExchangeFlags(player);
		if (flags[awardId.ToString()] != null)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"achievement.RewardGeted\"}");
			return;
		}
		uint points = MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.Points(player);
		if (points < row.Cost)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"tip.cash_not_enough\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint pointsSid = MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.PointsSid(activityId);
		MikuSB.GameServer.Game.Activity.ActivitySignService.SetAttr(player, pointsSid, points - row.Cost, sync);
		System.Text.Json.Nodes.JsonArray rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(new IReadOnlyList<uint>[] { row.Reward }, sync);
		flags[awardId.ToString()] = true;
		string flagJson = flags.ToString(Newtonsoft.Json.Formatting.None);
		player.SetStrAttr(MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.FlagGroup, MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.FlagSid, flagJson);
		sync.CustomStr[player.ToShiftedAttrKey(MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.FlagGroup, MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.FlagSid)] = flagJson;
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		MikuSB.Util.Logger.GetByClassName().Info($"感知巡游：uid={player.Uid} 兑换 {awardId} 花 {row.Cost} 点，剩 {points - row.Cost}，得 {rewarded.Count} 组奖励。");
		JObject response = new JObject
		{
			["nId"] = activityId,
			["nID"] = awardId,
			["tbRewards"] = JArray.Parse(rewarded.ToJsonString())
		};
		await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", response.ToString(), sync);
	}

	private static JObject LoadExchangeFlags(PlayerInstance player)
	{
		string raw = player.Data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.FlagGroup && x.Sid == MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.FlagSid)?.Val;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return new JObject();
		}
		try
		{
			return JObject.Parse(raw);
		}
		catch
		{
			return new JObject();
		}
	}

	private static JObject? LoadActivity(long id)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "activity", "activities.json");
			JArray source = JArray.Parse(File.ReadAllText(path));
			return source.OfType<JObject>().FirstOrDefault((JObject x) => x.Value<long?>("Id") == id);
		}
		catch
		{
			return null;
		}
	}

	private static async Task HandleQuestReward(Connection connection, long activityId, long questId)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		JObject row = LoadQuest(questId);
		JArray rewards = default;
		int num;
		if (row != null)
		{
			JToken jToken = row["Rewards"];
			rewards = jToken as JArray;
			num = ((rewards == null) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		if (num != 0)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerAttr attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == 7 && x.Sid == (uint)questId);
		int need = row.Value<int?>("CheckValue").GetValueOrDefault();
		if (attr == null || attr.Val >> 1 < (uint)Math.Max(0, need))
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"achievement.NotFinished\"}");
			return;
		}
		if ((attr.Val & 1) != 0)
		{
			await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", "{\"sErr\":\"achievement.RewardGeted\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JArray responseRewards = new JArray();
		foreach (JToken rewardNode in rewards)
		{
			JArray rewardRow = rewardNode as JArray;
			if (rewardRow != null && rewardRow.Count >= 5)
			{
				List<uint> reward = (from x in rewardRow.Select(ReadInt)
					select (uint)x).Take(5).ToList();
				if (reward.Count >= 5 && reward[0] != 0)
				{
					await IBLogic_BuyGoods.GrantRewardAsync(player, sync, reward);
					responseRewards.Add(rewardRow.DeepClone());
				}
			}
		}
		attr.Val |= 1u;
		sync.Custom[player.ToPackedAttrKey(7u, (uint)questId)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(7u, (uint)questId)] = attr.Val;
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		JObject rsp = new JObject
		{
			["nId"] = activityId,
			["nQuestId"] = questId,
			["tbRewards"] = responseRewards
		};
		await CallGSRouter.SendScript(connection, "ActivityQuest_GetReward", rsp.ToString(), sync);
	}

	private static JObject? LoadQuest(long id)
	{
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (!File.Exists(path))
			{
				return null;
			}
			JArray source = JArray.Parse(File.ReadAllText(path));
			return source.OfType<JObject>().FirstOrDefault((JObject x) => x.Value<long?>("ID") == id);
		}
		catch
		{
		}
		return null;
	}

	private static int ReadInt(JToken token)
	{
		if (token.Type == JTokenType.Integer)
		{
			return token.Value<int>();
		}
		if (token.Type == JTokenType.Float)
		{
			return (int)token.Value<decimal>();
		}
		int result;
		return int.TryParse(token.ToString(), out result) ? result : 0;
	}
}
