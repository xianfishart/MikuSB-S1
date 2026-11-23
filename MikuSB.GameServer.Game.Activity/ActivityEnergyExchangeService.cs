using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 「感知巡游」（sClass = energy_exchange）——第 432 节。
///
/// 客户端 EnergyExchange.lua 里：
///   · 巡游度 = Activity.GetDiyData(actId, CostEnergy)  → 属性 50/(actId*10+2+1)；
///   · 领取标记 = me:GetStrAttribute(54, 1) 里的一段 JSON（awardId → true）；
///   · 奖励表 = Resources/activity/energy_exchange/graph.json，
///     每格 [awardId, cost, [itemId, count, ...5 段]]。
/// 官方玩法是「消耗感知（体力）换等量智识巡游度」，本服一直没实现累积，
/// 所以界面永远是 0，点任何奖励都提示「智识巡游度不足」。
/// 这里补上 1:1 累积（扣体力时同步加点），并对外提供奖励表读取。
/// </summary>
public static class ActivityEnergyExchangeService
{
	public const string ClassName = "energy_exchange";

	public const uint CostEnergyTaskId = 1u;

	public const uint FlagGroup = 54u;

	public const uint FlagSid = 1u;

	public static ActivityExcel Act()
	{
		foreach (ActivityExcel value in GameData.ActivityData.Values)
		{
			if (string.Equals(value.Class, ClassName, StringComparison.OrdinalIgnoreCase))
			{
				return value;
			}
		}
		return null;
	}

	public static uint PointsSid(uint activityId)
	{
		return activityId * 10u + 2u + CostEnergyTaskId;
	}

	public static uint Points(PlayerInstance player)
	{
		ActivityExcel excel = Act();
		return excel == null ? 0u : player.Attributes.GetValue(50u, PointsSid(excel.Id));
	}

	public static bool IsEnergyActivity(uint activityId)
	{
		ActivityExcel excel = Act();
		return excel != null && excel.Id == activityId;
	}

	/// <summary>扣体力时同步累积巡游度（1 点体力 = 1 点巡游度）。</summary>
	public static void AccrueFromVigor(PlayerInstance player, uint spent, NtfSyncPlayer sync)
	{
		if (player == null || spent == 0)
		{
			return;
		}
		// 第 438 节：消耗感知类活动任务（星祈心愿「完成公务」等，Type 4）按点数累积。
		// 放在巡游度判断之前 —— 没开巡游活动的期数，这些任务照样要能推进。
		ActivityQuestService.Add(player, 4, (int)spent, sync);
		ActivityExcel excel = Act();
		if (excel == null)
		{
			return;
		}
		uint sid = PointsSid(excel.Id);
		uint cur = player.Attributes.GetValue(50u, sid);
		ActivitySignService.SetAttr(player, sid, cur + spent, sync);
	}

	/// <summary>graph.json 里的一格：[awardId, cost, reward[]]。</summary>
	public sealed class ExchangeRow
	{
		public uint AwardId;
		public uint Cost;
		public List<uint> Reward = new List<uint>();
	}

	private static readonly Lazy<Dictionary<uint, ExchangeRow>> Rows = new Lazy<Dictionary<uint, ExchangeRow>>(LoadRows);

	public static ExchangeRow Find(uint awardId)
	{
		return Rows.Value.TryGetValue(awardId, out ExchangeRow row) ? row : null;
	}

	private static Dictionary<uint, ExchangeRow> LoadRows()
	{
		Dictionary<uint, ExchangeRow> map = new Dictionary<uint, ExchangeRow>();
		try
		{
			string path = Path.Combine(MikuSB.Util.ConfigManager.Config.Path.ResourcePath, "activity", "energy_exchange", "graph.json");
			if (!File.Exists(path))
			{
				return map;
			}
			JsonNode node = JsonNode.Parse(File.ReadAllText(path));
			if (!(node is JsonArray rows))
			{
				return map;
			}
			foreach (JsonNode row in rows)
			{
				if (!(row is JsonObject obj))
				{
					continue;
				}
				foreach (KeyValuePair<string, JsonNode> cell in obj)
				{
					if (!(cell.Value is JsonArray { Count: >= 3 } arr))
					{
						continue;
					}
					uint awardId = ReadUint(arr[0]);
					uint cost = ReadUint(arr[1]);
					if (awardId == 0 || !(arr[2] is JsonArray rewardArr) || rewardArr.Count < 5)
					{
						continue;
					}
					ExchangeRow item = new ExchangeRow
					{
						AwardId = awardId,
						Cost = cost
					};
					for (int i = 0; i < 5; i++)
					{
						item.Reward.Add(ReadUint(rewardArr[i]));
					}
					map[awardId] = item;
				}
			}
			MikuSB.Util.Logger.GetByClassName().Info($"感知巡游：从 {path} 读到 {map.Count} 个兑换格。");
		}
		catch (Exception ex)
		{
			MikuSB.Util.Logger.GetByClassName().Warn("感知巡游配置读取失败: " + ex.Message);
		}
		return map;
	}

	private static uint ReadUint(JsonNode node)
	{
		if (node is JsonValue value && value.TryGetValue<uint>(out uint result))
		{
			return result;
		}
		return uint.TryParse(node?.ToString() ?? string.Empty, out uint parsed) ? parsed : 0u;
	}
}
