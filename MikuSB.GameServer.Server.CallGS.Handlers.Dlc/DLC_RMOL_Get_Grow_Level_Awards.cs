using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// 第 449 节：第 32 期「绘梦沙盘」成长奖励（繁荣嘉奖 → 缤纷补给）领取。
///
/// 客户端 umg_rmol_reward 点某个等级奖励时发
/// {"Levels":[2],"Session":1}，服务端原先没有 handler
/// （日志 "No handler for CallGS API: DLC_RMOL_Get_Grow_Level_Awards"），点了没反应。
///
/// 客户端两处读取点（DLC_RMOL.lua）：
///   1) 是否已领：GrowLevel.IsGot()
///        me:GetAttribute(DLC_RMOL.AttrGID=172, Session.ID*SessionRange(100) + GrowLevelAwardGainFlag(0) + Level) > 0
///      也就是每个成长等级单独占一个属性槽：等级 1 → 172/101，等级 2 → 172/102 …
///   2) 是否可领：GrowLevel.IsActive()
///        Session:CurGrowLevel().Level >= Level
///      CurGrowLevel 由累计经验（172/100）按 config/grow.json 的 NextLevelEXP 逐级扣减算出。
///
/// 奖励表同样在客户端 Resources/_Mod/RMOL/config/grow.json 的 Award 字段（五行数组）。
/// 回包 {"Levels":[已领等级...], "Awards":[[G,D,P,L,N], ...]}，
/// 对应 s2c.tbCmds["DLC_RMOL_Get_Grow_Level_Awards"] 触发的
/// "DLC_RMOL_Get_Grow_Level_Awards_Return"(Levels, Awards)。
/// </summary>
[CallGSApi("DLC_RMOL_Get_Grow_Level_Awards")]
public class DLC_RMOL_Get_Grow_Level_Awards : ICallGSHandler
{
	private static readonly Logger Log = new Logger("DLC_RMOL_GrowAward");

	private const uint GrowAttrGid = 172u;

	private const int SessionRange = 100;

	private const int GrowLevelAwardGainFlag = 0;

	private sealed class GrowRow
	{
		public int SessionId;

		public int Level;

		public int NextLevelExp;

		public List<IReadOnlyList<uint>> Award = new List<IReadOnlyList<uint>>();
	}

	private static readonly object CacheLock = new object();

	private static readonly List<GrowRow> Cache = new List<GrowRow>();

	private static DateTime cacheStamp = DateTime.MinValue;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		int sessionId = ReadInt(request["Session"], request["session"]);
		if (sessionId <= 0)
		{
			sessionId = 1;
		}
		List<int> requestLevels = new List<int>();
		if (request["Levels"] is JsonArray levelArray)
		{
			foreach (JsonNode? node in levelArray)
			{
				int value = ReadInt(node);
				if (value > 0)
				{
					requestLevels.Add(value);
				}
			}
		}
		else
		{
			int single = ReadInt(request["Level"], request["level"]);
			if (single > 0)
			{
				requestLevels.Add(single);
			}
		}

		PlayerInstance? player = connection.Player;
		JsonArray claimedLevels = new JsonArray();
		JsonArray awards = new JsonArray();
		if (player == null || requestLevels.Count == 0)
		{
			JsonObject bad = new JsonObject
			{
				["Levels"] = claimedLevels,
				["Awards"] = awards
			};
			await CallGSRouter.SendScript(connection, "DLC_RMOL_Get_Grow_Level_Awards", bad.ToJsonString());
			return;
		}

		List<GrowRow> rows = GetGrowRows();
		uint totalExp = player.Attributes.GetValue(GrowAttrGid, (uint)(sessionId * SessionRange));
		int growLevel = CalcGrowLevel(rows, totalExp);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<IReadOnlyList<uint>> grantRows = new List<IReadOnlyList<uint>>();

		foreach (int level in requestLevels)
		{
			GrowRow? row = rows.Find((GrowRow x) => x.SessionId == sessionId && x.Level == level);
			if (row == null || row.Level > growLevel || row.Award.Count == 0)
			{
				continue;
			}
			int sid = sessionId * SessionRange + GrowLevelAwardGainFlag + level;
			PlayerAttr flagAttr = player.Attributes.GetOrCreate(GrowAttrGid, (uint)sid);
			if (flagAttr.Val > 0)
			{
				continue;
			}
			foreach (IReadOnlyList<uint> reward in row.Award)
			{
				grantRows.Add(reward);
			}
			flagAttr.Val = 1u;
			player.Attributes.SyncTo(sync, flagAttr);
			claimedLevels.Add(level);
		}

		if (grantRows.Count > 0)
		{
			awards = await player.RewardManager.GrantConfiguredRewardsAsync(grantRows, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			if (player.InventoryManager != null)
			{
				DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			}
		}

		Log.Info($"uid={player.Uid} rmol grow award session={sessionId} req=[{string.Join(',', requestLevels)}] " +
			$"claimed={claimedLevels.Count} awards={awards.Count} totalExp={totalExp} growLv={growLevel}");

		JsonObject rsp = new JsonObject
		{
			["Levels"] = claimedLevels,
			["Awards"] = awards
		};
		await CallGSRouter.SendScript(connection, "DLC_RMOL_Get_Grow_Level_Awards", rsp.ToJsonString(), sync);
	}

	private static int CalcGrowLevel(List<GrowRow> rows, uint totalExp)
	{
		int exp = (int)Math.Min(int.MaxValue, totalExp);
		int level = 1;
		foreach (GrowRow row in rows)
		{
			if (row.Level <= level)
			{
				continue;
			}
			if (row.NextLevelExp <= 0 || exp < row.NextLevelExp)
			{
				break;
			}
			exp -= row.NextLevelExp;
			level = row.Level;
		}
		return level;
	}

	private static List<GrowRow> GetGrowRows()
	{
		lock (CacheLock)
		{
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "_Mod", "RMOL", "config", "grow.json");
				DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
				if (stamp == cacheStamp)
				{
					return Cache;
				}
				Cache.Clear();
				cacheStamp = stamp;
				if (!File.Exists(path))
				{
					return Cache;
				}
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
				foreach (JsonElement element in doc.RootElement.EnumerateArray())
				{
					GrowRow row = new GrowRow
					{
						SessionId = ReadIntElement(element, "SessionID"),
						Level = ReadIntElement(element, "Level"),
						NextLevelExp = ReadIntElement(element, "NextLevelEXP")
					};
					if (element.TryGetProperty("Award", out JsonElement award) && award.ValueKind == JsonValueKind.Array && award.GetArrayLength() >= 5)
					{
						List<uint> values = new List<uint>(5);
						bool ok = true;
						for (int i = 0; i < 5; i++)
						{
							if (!TryReadUInt(award[i], out uint v))
							{
								ok = false;
								break;
							}
							values.Add(v);
						}
						if (ok && values[4] != 0)
						{
							row.Award.Add(values);
						}
					}
					Cache.Add(row);
				}
				Cache.Sort((GrowRow a, GrowRow b) => a.Level.CompareTo(b.Level));
				return Cache;
			}
			catch (Exception ex)
			{
				Log.Info($"[warn] read grow.json failed: {ex.Message}");
				return Cache;
			}
		}
	}

	private static int ReadIntElement(JsonElement row, string name)
	{
		return row.TryGetProperty(name, out JsonElement value) && TryReadUInt(value, out uint v) ? (int)v : 0;
	}

	private static bool TryReadUInt(JsonElement value, out uint result)
	{
		result = 0u;
		if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out result))
		{
			return true;
		}
		if (value.ValueKind == JsonValueKind.String)
		{
			string? text = value.GetString();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return uint.TryParse(text.Trim().Trim('[', ']', '"'), out result);
			}
		}
		return false;
	}

	private static int ReadInt(params JsonNode?[] nodes)
	{
		foreach (JsonNode? node in nodes)
		{
			if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out double value))
			{
				return (int)value;
			}
		}
		return 0;
	}
}
