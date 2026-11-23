using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

/// <summary>
/// 进关 / 速战的体力扣费（修复记录第 181、182、221 节）。
/// 注意：这个方法曾被历史改动退化成 `return true` 的空实现，
/// 表现就是「进关卡、速战都不掉体力」。任何回退后都要确认这里仍是扣费版。
/// </summary>
internal static class ChapterVigor
{
	private static readonly Logger Log = new Logger("Vigor");

	private static Dictionary<uint, uint> _costById;

	private static readonly object _lock = new object();

	private static readonly string[] IdKeys = new string[7] { "nID", "nLevelID", "nLevelId", "nId", "nLevel", "levelId", "nStageID" };

	public static async Task<bool> TryConsume(Connection connection, JsonObject request, NtfSyncPlayer sync, string responseApi)
	{
		PlayerInstance player = connection?.Player;
		if (player?.Data == null)
		{
			return true;
		}
		uint cost = ResolveCost(request);
		return await ConsumeCost(connection, player, cost, sync, responseApi);
	}

	/// <summary>
	/// 实际扣费（进关与速战共用）。体力不足时回 vigor_not_enough 并返回 false。
	/// </summary>
	public static async Task<bool> ConsumeCost(Connection connection, PlayerInstance player, uint cost, NtfSyncPlayer sync, string responseApi)
	{
		if (player?.Data == null || cost == 0)
		{
			return true;
		}
		if (player.Data.Vigor < cost)
		{
			Log.Info($"uid={player.Uid} {responseApi} cost={cost} vigor={player.Data.Vigor} -> vigor_not_enough");
			await CallGSRouter.SendScript(connection, responseApi, "{\"sErr\":\"error.vigor_not_enough\"}");
			return false;
		}
		player.Data.Vigor -= cost;
		// 第 432 节：感知巡游（energy_exchange）按「消耗感知 = 等量智识巡游度」累积。
		MikuSB.GameServer.Game.Activity.ActivityEnergyExchangeService.AccrueFromVigor(player, cost, sync);
		if (sync != null)
		{
			sync.Core[2u] = player.Data.Vigor;
			sync.Core[4u] = player.Attributes.GetValue(9900u, 1u);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"uid={player.Uid} {responseApi} cost={cost} vigor={player.Data.Vigor}/{VigorRecovery.MaxOf(player)}");
		return true;
	}

	public static uint GetCost(int levelId)
	{
		LoadCosts().TryGetValue((uint)levelId, out uint value);
		return value;
	}

	private static uint ResolveCost(JsonObject request)
	{
		if (request == null)
		{
			return 0u;
		}
		Dictionary<uint, uint> costs = LoadCosts();
		if (costs.Count == 0)
		{
			return 0u;
		}
		foreach (string key in IdKeys)
		{
			uint levelId = ReadUint(request[key]);
			if (levelId != 0 && costs.TryGetValue(levelId, out uint value))
			{
				return value;
			}
		}
		// 键名不符时兜底：扫请求顶层的整数值，能对上关卡 id 就按它扣。
		foreach (KeyValuePair<string, JsonNode> pair in request)
		{
			uint candidate = ReadUint(pair.Value);
			if (candidate != 0 && costs.TryGetValue(candidate, out uint value2))
			{
				return value2;
			}
		}
		return 0u;
	}

	private static uint ReadUint(JsonNode node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out double value) && value >= 1.0 && value < 4294967295.0)
		{
			return (uint)value;
		}
		return 0u;
	}

	private static Dictionary<uint, uint> LoadCosts()
	{
		lock (_lock)
		{
			if (_costById != null)
			{
				return _costById;
			}
			Dictionary<uint, uint> dict = new Dictionary<uint, uint>();
			try
			{
				AddCosts(dict, GameData.ChapterLevelData);
				AddCosts(dict, GameData.DailyLevelData);
				AddCosts(dict, GameData.RoleLevelData);
				AddCosts(dict, GameData.TowerLevelData);
				AddCosts(dict, GameData.TowerEventLevelData);
				AddCosts(dict, GameData.DlcChapterLevelData);
				AddCosts(dict, GameData.RepressionLevelData);
				AddCosts(dict, GameData.VirCaptureLevelData);
				AddCosts(dict, GameData.ElementLevelData);
			}
			catch (Exception ex)
			{
				Log.Warn("load vigor costs failed: " + ex.Message);
			}
			Log.Info($"vigor cost table ready: {dict.Count} levels with cost");
			_costById = dict;
			return dict;
		}
	}

	private static void AddCosts<T>(Dictionary<uint, uint> dict, IEnumerable<KeyValuePair<uint, T>> rows) where T : class, ILevelRewardConfig
	{
		foreach (KeyValuePair<uint, T> row in rows)
		{
			uint cost = row.Value.ConsumeVigor();
			if (cost != 0 && !dict.ContainsKey(row.Key))
			{
				dict[row.Key] = cost;
			}
		}
	}
}
