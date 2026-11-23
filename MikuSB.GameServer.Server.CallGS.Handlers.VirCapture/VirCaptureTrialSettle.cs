using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「试炼吧」结算（第 352 节）。
///
/// 为什么单独写一个类而不是复用 Settlement\VirCaptureTrialLevelSettlementHandler：
///   那个类继承 GenericSettlementHandler，而 Chapter_DealLevelSettlement 里
///   的「按 sCmd 派发专用处理器」分支明确排除了 GenericSettlementHandler，
///   于是试炼结算永远落到最后的默认分支（把 tbParam 原样回吐），
///   结果页「本次测评 / 测评记录」只能显示 0。这里按 sCmd 显式接入。
///
/// 分数口径来自 Resources/dlc/vircapture/trial.json 的 Score 字段：
///   "Score": [[2, [10,100], [30,1000], [50,2500], [80,5000], [110,6000], ...]]
///   首元素是计分模式（2 = 按击杀数），后面每个 [击杀门槛, 得分]，
///   取「击杀数 ≥ 门槛」里门槛最高的那一档得分。该表服务端本来没加载，
///   这里直接读 json 文件并缓存（与 CommandNewStar 读 levellist.json 同样的做法）。
/// </summary>
public static class VirCaptureTrialSettle
{
	private static readonly Logger Logger = new Logger("VirCaptureTrial");

	/// <summary>最高分落库用的属性组：128 组的 20000+ 区间（10000~19000 是地图槽位，20000 之后空闲）。</summary>
	private const uint BestScoreSidBase = 20000u;

	/// <summary>
	/// 「测评记录 / 记录阵容」在客户端是字符串属性 VirCapture.Str_GroupId=57 / Str_TrialFormation=2，
	/// 由 VirCapture.GetTrialFormationData() 读出来 json.decode 成数组；客户端只读三段（Lua 1-based）：
	///   [1]        = 历史最高分（结算页「测评记录」）
	///   [18..20]   = 3 个试炼角色（Split("-") 后 tonumber 丢给 me:GetTrialCard）
	///   [1554..1558] = 5 个怪物卡（Item.tbMonsterCard[key]，键形如 "18-1-21-1"，本期先不填）
	/// 数组长度按客户端的最大下标给 1558，其余留 0（第 353 节）。
	/// </summary>
	private const uint StrGid = 57u;

	private const uint StrSidTrialFormation = 2u;

	private const int FormationArrayLen = 1558;

	private const int FormationCardBase = 17;

	/// <summary>主界面「试炼积分」= 数值属性 128/7（VirCapture.GroupId=128 / TrialPoints=7）。</summary>
	private const uint TrialPointsGid = 128u;

	private const uint TrialPointsSid = 7u;

	private static readonly object Gate = new object();

	private static Dictionary<uint, List<KeyValuePair<int, uint>>>? _cache;

	private static Dictionary<uint, List<uint>>? _cardCache;

	private static string TrialJsonPath => Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "vircapture", "trial.json");

	public static JsonObject Settle(PlayerInstance player, JsonObject? param, NtfSyncPlayer sync)
	{
		uint trialId = ReadUint(param, "nID", "nId", "nLevelID", "nTrialID", "ID", "id");
		int kills = ReadKills(param);
		uint score = ComputeScore(trialId, kills);
		uint best = score;
		uint oldScore = 0u;
		bool newRecord = true;
		if (player != null && trialId != 0)
		{
			PlayerAttr attr = GetOrCreateAttr(player.Data, 128u, BestScoreSidBase + trialId);
			uint old = attr.Val;
			oldScore = old;
			best = Math.Max(old, score);
			newRecord = score > old;
			if (newRecord)
			{
				attr.Val = score;
				SyncAttr(sync, player, attr);
			}
			// 顺手把「已通关」写进 21/22 组，和关卡/塔同一套判据（客户端 IsPass 会用到）。
			PlayerAttr state = GetOrCreateAttr(player.Data, 21u, trialId);
			state.Val |= 0x100u;
			SyncAttr(sync, player, state);
			PlayerAttr pass = GetOrCreateAttr(player.Data, 22u, trialId);
			pass.Val = Math.Max(1u, pass.Val + 1);
			SyncAttr(sync, player, pass);
			// 「测评记录 / 记录阵容」：客户端读的是字符串属性 57/2，官服由服务端在结算时写。
			// 破纪录就覆盖；从未写过（首次）也写一次，否则记录永远是 0、阵容永远空。
			string existing = player.Attributes.GetStringValue(StrGid, StrSidTrialFormation) ?? string.Empty;
			bool hasRecord = existing.Length > 2 && existing.Trim() != "[]";
			if (newRecord || !hasRecord)
			{
				WriteRecord(player, sync, trialId, best);
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		Logger.Info($"VirCaptureTrial settlement uid={player?.Uid} trialId={trialId} kills={kills} score={score} best={best} newRecord={newRecord}");
		return new JsonObject
		{
			["tbAward"] = new JsonArray(),
			["tbReward"] = new JsonArray(),
			["nScore"] = (int)score,
			["nBestScore"] = (int)best,
			["nOldScore"] = (int)oldScore,
			["bNewRecord"] = newRecord,
			["bSuccess"] = true
		};
	}

	/// <summary>击杀数：客户端 tbKill 是数组（如 [94,5]），求和即本局击杀。</summary>
	private static int ReadKills(JsonObject? param)
	{
		if (param == null)
		{
			return 0;
		}
		int sum = 0;
		if (param["tbKill"] is JsonArray kills)
		{
			foreach (JsonNode node in kills)
			{
				sum += (int)ReadUint(node);
			}
		}
		if (sum > 0)
		{
			return sum;
		}
		int direct = (int)ReadUint(param, "nKill", "nKillNum", "nKillCount");
		if (direct > 0)
		{
			return direct;
		}
		// 兜底：LevelFinish 日志里第 6 项是总击杀（实测 182 这种累计值，仅当 tbKill 缺失时使用）。
		if (param["tbLog"]?["LevelFinish"] is JsonArray log && log.Count > 5)
		{
			return (int)ReadUint(log[5]);
		}
		return 0;
	}

	public static uint ComputeScore(uint trialId, int kills)
	{
		List<KeyValuePair<int, uint>> tiers = LoadTiers().TryGetValue(trialId, out List<KeyValuePair<int, uint>> list)
			? list
			: new List<KeyValuePair<int, uint>>();
		if (tiers.Count == 0)
		{
			// 别静默算成 0：表里没有这一期时明确告警，方便下次一眼看出是配置的问题。
			Logger.Warn($"no score tiers for trial {trialId} (trial.json 里缺这一行或 Score 为空)");
		}
		uint score = 0u;
		foreach (KeyValuePair<int, uint> tier in tiers)
		{
			if (kills >= tier.Key)
			{
				score = Math.Max(score, tier.Value);
			}
		}
		return score;
	}

	/// <summary>
	/// 写「试炼记录」：字符串属性 57/2 = 一个 1558 长的数组。
	/// 客户端 VirCapture.GetTrialFormationData() 读它并 json.decode，
	/// 结算面板从中取 [1]（测评记录）与 [18..20]（记录阵容的角色）。
	/// 角色要写成**字符串**：客户端会 Split(value, "-") 再 tonumber，数字会走到 Split 的类型错误分支。
	/// </summary>
	public static void WriteRecord(PlayerInstance player, NtfSyncPlayer? sync, uint trialId, uint score)
	{
		if (player == null)
		{
			return;
		}
		List<uint> cards = LoadTrialCards().TryGetValue(trialId, out List<uint> value) ? value : new List<uint>();
		JsonArray arr = new JsonArray();
		for (int i = 0; i < FormationArrayLen; i++)
		{
			arr.Add(0);
		}
		arr[0] = (int)score;
		for (int i = 0; i < cards.Count && i < 3; i++)
		{
			arr[FormationCardBase + i] = cards[i].ToString(CultureInfo.InvariantCulture);
		}
		string json = arr.ToJsonString();
		player.SetStrAttr(StrGid, StrSidTrialFormation, json);
		if (sync != null)
		{
			sync.CustomStr[player.ToShiftedAttrKey(StrGid, StrSidTrialFormation)] = json;
		}
		// 主界面「试炼积分」：按历史最高分往上抬，不回退。
		PlayerAttr points = GetOrCreateAttr(player.Data, TrialPointsGid, TrialPointsSid);
		if (score > points.Val)
		{
			points.Val = score;
			if (sync != null)
			{
				SyncAttr(sync, player, points);
			}
		}
		Logger.Info($"VirCaptureTrial record uid={player.Uid} trialId={trialId} score={score} cards=[{string.Join(",", cards)}] len={json.Length}");
	}

	/// <summary>读 trial.json 的 TrialCard（每个试炼固定的 3 张角色卡 id，如 100001/100002/100009）。</summary>
	private static Dictionary<uint, List<uint>> LoadTrialCards()
	{
		lock (Gate)
		{
			if (_cardCache != null)
			{
				return _cardCache;
			}
			Dictionary<uint, List<uint>> map = new Dictionary<uint, List<uint>>();
			try
			{
				if (File.Exists(TrialJsonPath))
				{
					using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(TrialJsonPath));
					foreach (JsonElement row in doc.RootElement.EnumerateArray())
					{
						if (!row.TryGetProperty("ID", out JsonElement idEl) || !idEl.TryGetUInt32(out uint id))
						{
							continue;
						}
						List<uint> cards = new List<uint>();
						if (row.TryGetProperty("TrialCard", out JsonElement cardEl) && cardEl.ValueKind == JsonValueKind.Array)
						{
							foreach (JsonElement item in cardEl.EnumerateArray())
							{
								if (item.TryGetUInt32(out uint card))
								{
									cards.Add(card);
								}
							}
						}
						map[id] = cards;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warn("load trial.json TrialCard failed: " + ex.Message);
			}
			_cardCache = map;
			return _cardCache;
		}
	}

	private static Dictionary<uint, List<KeyValuePair<int, uint>>> LoadTiers()
	{
		lock (Gate)
		{
			if (_cache != null)
			{
				return _cache;
			}
			Dictionary<uint, List<KeyValuePair<int, uint>>> map = new Dictionary<uint, List<KeyValuePair<int, uint>>>();
			try
			{
				if (File.Exists(TrialJsonPath))
				{
					using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(TrialJsonPath));
					foreach (JsonElement row in doc.RootElement.EnumerateArray())
					{
						if (!row.TryGetProperty("ID", out JsonElement idEl) || !idEl.TryGetUInt32(out uint id))
						{
							continue;
						}
						List<KeyValuePair<int, uint>> tiers = new List<KeyValuePair<int, uint>>();
						if (row.TryGetProperty("Score", out JsonElement scoreEl) && scoreEl.ValueKind == JsonValueKind.Array && scoreEl.GetArrayLength() > 0)
						{
							// 官方表是"多包一层"的：Score = [[2, [10,100], [30,1000], ...]]，
							// 外层只有 1 个元素，真正的内容在内层；先把这层壳剥掉（第 352 节踩过的坑）。
							JsonElement tierList = scoreEl;
							while (tierList.ValueKind == JsonValueKind.Array && tierList.GetArrayLength() == 1
								&& tierList[0].ValueKind == JsonValueKind.Array)
							{
								tierList = tierList[0];
							}
							// 内层第一个元素是计分模式（2 = 按击杀数），从第 2 项起才是 [门槛, 分数]
							int start = (tierList.GetArrayLength() > 0 && tierList[0].ValueKind == JsonValueKind.Number) ? 1 : 0;
							for (int i = start; i < tierList.GetArrayLength(); i++)
							{
								JsonElement pair = tierList[i];
								if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
								{
									continue;
								}
								int threshold = pair[0].TryGetInt32(out int t) ? t : 0;
								uint value = pair[1].TryGetUInt32(out uint v) ? v : 0u;
								tiers.Add(new KeyValuePair<int, uint>(threshold, value));
							}
						}
						map[id] = tiers;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warn("load trial.json failed: " + ex.Message);
			}
			_cache = map;
			return _cache;
		}
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node == null)
		{
			return 0u;
		}
		if (node is JsonValue value && value.TryGetValue<uint>(out uint direct))
		{
			return direct;
		}
		return uint.TryParse(node.ToString(), out uint parsed) ? parsed : 0u;
	}

	private static uint ReadUint(JsonObject? obj, params string[] keys)
	{
		if (obj == null)
		{
			return 0u;
		}
		foreach (string key in keys)
		{
			uint v = ReadUint(obj[key]);
			if (v != 0)
			{
				return v;
			}
		}
		return 0u;
	}

	private static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr attr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (attr != null)
		{
			return attr;
		}
		attr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid
		};
		data.Attrs.Add(attr);
		return attr;
	}

	private static void SyncAttr(NtfSyncPlayer sync, PlayerInstance player, PlayerAttr attr)
	{
		sync.Custom[player.ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
	}
}
