using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

/// <summary>
/// 增益试炼（悖论迷宫 · 验证战场里刷「部件」的那个玩法）关卡结算。
///
/// 第 312 节：以前这里恒回 {"bSuccess":true,"tbReward":[]}，等于什么都不发 ——
/// 客户端 umg_rogue25_settlement.OnOpen 读的是
///   rsp.tbAward / rsp.tbUavPart / rsp.nWaves / rsp.nBuffBattleId
/// 里 tbUavPart 为空 → 结算页的部件栏一片空白，属性槽里也没写东西，
/// 表现为「通关后没有部件奖励」。
///
/// 口径全部来自客户端配置：
///   · dlc/rogue3d/score_model/materialbuffdrop.json
///       按 (MaterialBuffBattleID, Waves) 取行：
///         ComponentReward = [数量, [可出部件 Type 列表], [[品质, 权重], ...]]
///         CoinReward      = [[G,D,P,L,N], ...]
///   · dlc/rogue3d/uav/uav_part.json  部件表（ID = (Type-1)*9 + (3-品质)*3 + SubType）
///   · dlc/rogue3d/uav/uav_buff.json  部件词条表（GroupId 分组，Weight 权重，ValueRange 数值区间）
///
/// 部件不是物品，而是玩家属性组 124 的位打包数据（Rogue3DLogic.PartBegin=3105 ~ PartEnd=3365，
/// 共 261 个槽），每槽 32 位：
///   bit 0..5   部件 ID (1..36)
///   bit 6..17  词条 ID (uav_buff.ID)
///   bit 18..27 词条数值（Type=1 为属性数值，Type=2 为强度档）
///   bit 28..31 装配槽（0 = 未装配）
/// 客户端 Rogue3DUav.GetUavPart(idx) 就是这么解的，结算页把 tbUavPart 里的相对下标原样传进去。
/// </summary>
[CallGSApi("Rogue3DScoreModel_BuffLevelSettlement")]
public class Rogue3DScoreModel_BuffLevelSettlement : ICallGSHandler
{
	private const uint AttrGid = 124u;

	private const int PartBegin = 3105;

	private const int PartEnd = 3365;

	private static readonly Logger Logger = new Logger("Rogue3DBuff");

	private static readonly object Gate = new object();

	/// <summary>
	/// 已经结算过的最大波次，防止同一个结算包重发（断线补偿 / 重复点击）把部件刷两遍。
	/// 键用「进入关卡时服务端下发的 nSeed」而不是 battleId —— 每局 seed 不同，
	/// 玩家重开一局再打第 1 波不会因为上次打过而被吞掉。
	/// </summary>
	private static readonly Dictionary<(long Uid, long Seed), int> Settled = new Dictionary<(long, long), int>();

	private static List<DropRow>? _drops;

	private static Dictionary<long, Dictionary<uint, int>>? _battleByLevel;

	private static Dictionary<uint, PartCfg>? _parts;

	private static Dictionary<uint, List<BuffCfg>>? _buffsByGroup;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject root = SafeParse(param);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonObject resp = await SettleAsync(player, root, sync);
		await CallGSRouter.SendScript(connection, "Rogue3DScoreModel_BuffLevelSettlement", resp.ToJsonString(), sync);
	}

	/// <summary>
	/// 真正的结算：由 Chapter_DealLevelSettlement 的分发分支直接调用（客户端结算包走
	/// Reconnect.Send_SettleInfo → REQ_DealLevelSettlement → sCmd=Rogue3DScoreModel_BuffLevelSettlement）。
	/// 发奖与属性同步都写进调用方传进来的 sync，由外层统一回包。
	/// </summary>
	public static async ValueTask<JsonObject> SettleAsync(PlayerInstance player, JsonNode? tbParam, NtfSyncPlayer sync)
	{
		JsonObject root = tbParam as JsonObject ?? new JsonObject();
		uint battleId = ReadUint(root["nBattleId"]);
		uint levelId = ReadUint(root["nID"]);
		uint seed = ReadUint(root["nSeed"]);
		int waves = (int)ReadUint(root["nWaves"]);
		Log($"settle uid={player.Uid} battle={battleId} level={levelId} seed={seed} waves={waves} raw={Trim(tbParam?.ToJsonString(), 400)}");
		if (battleId == 0)
		{
			battleId = BattleByLevel(levelId);
		}
		int maxWaves = MaxWaves(battleId);
		if (waves < 1)
		{
			waves = 1;
		}
		if (maxWaves > 0 && waves > maxWaves)
		{
			waves = maxWaves;
		}
		JsonArray award = new JsonArray();
		JsonArray uavPart = new JsonArray();
		// 第 313 节：客户端整局只在通关时发一次结算包（nWaves = 已完成波数，实测难度 3 通关
		// 是 waves=9），不是每波都发。若只按最后一波取行发奖，1~8 波的配置等于白写
		// （实测通关只拿到 1 件部件 + 60 战场经验）。这里从「上次发到的波次 +1」补到 waves，
		// 同类金币累加成一条再发，部件逐波抽取。
		int fromWave = ReserveWaves(player, battleId, seed, waves);
		if (fromWave > 0)
		{
			Dictionary<long, uint[]> coinSum = new Dictionary<long, uint[]>();
			List<long> coinOrder = new List<long>();
			int partCount = 0;
			for (int w = fromWave; w <= waves; w++)
			{
				DropRow? waveRow = FindDrop(battleId, (uint)w);
				if (waveRow == null)
				{
					continue;
				}
				foreach (uint[] coin in waveRow.Coins)
				{
					long key = CoinKey(coin);
					if (coinSum.TryGetValue(key, out uint[]? agg))
					{
						agg[4] += coin[4];
					}
					else
					{
						coinSum[key] = new uint[5] { coin[0], coin[1], coin[2], coin[3], coin[4] };
						coinOrder.Add(key);
					}
				}
				List<uint> idx = GrantParts(player, sync, waveRow);
				foreach (uint i in idx)
				{
					uavPart.Add((int)i);
				}
				partCount += idx.Count;
				Log($"  wave {w}: parts={idx.Count} [{string.Join(",", idx)}]");
			}
			foreach (long key in coinOrder)
			{
				uint[] coin = coinSum[key];
				if (await GrantOneAsync(player, sync, coin))
				{
					award.Add(ToJsonArray(coin));
				}
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			Logger.Info($"buff settle uid={player.Uid} battle={battleId} wave={waves} from={fromWave} award={award.Count} parts={partCount}");
			Log($"  -> waves {fromWave}..{waves} award={award.Count} parts={partCount} [{string.Join(",", uavPart)}]");
		}
		else
		{
			Log($"  -> already settled up to wave {waves} (battle={battleId})");
		}
		JsonObject resp = new JsonObject
		{
			["bSuccess"] = true,
			["nBuffBattleId"] = battleId,
			["nWaves"] = waves,
			["tbAward"] = award,
			["tbUavPart"] = uavPart,
			["tbMonsterDropRiki"] = new JsonArray(),
			["tbFight"] = new JsonObject(),
			["tbUnlockBuffs"] = new JsonArray(),
			["nNewScore"] = 0,
			["nOldScore"] = 0,
			["tbChallengeMap"] = new JsonObject()
		};
		return resp;
	}

	// ────────────────────────────── 部件发放 ──────────────────────────────

	private static List<uint> GrantParts(PlayerInstance player, NtfSyncPlayer sync, DropRow row)
	{
		List<uint> result = new List<uint>();
		Dictionary<uint, PartCfg>? parts = Parts();
		Dictionary<uint, List<BuffCfg>>? buffs = BuffsByGroup();
		if (parts == null || parts.Count == 0 || buffs == null)
		{
			Logger.Warn("uav_part/uav_buff 配置缺失，无法发放部件");
			return result;
		}
		int slot = FindFreeSlot(player);
		for (int i = 0; i < row.PartCount; i++)
		{
			if (slot < 0)
			{
				Logger.Warn($"部件槽已满（{PartEnd - PartBegin} 个），本次少发 {row.PartCount - i} 个 uid={player.Uid}");
				break;
			}
			int quality = PickQuality(row);
			int type = row.Types.Count == 0 ? 1 : row.Types[Random.Shared.Next(row.Types.Count)];
			int subType = Random.Shared.Next(1, 4);
			uint partId = (uint)((type - 1) * 9 + (3 - quality) * 3 + subType);
			if (!parts.TryGetValue(partId, out PartCfg? part))
			{
				continue;
			}
			if (!buffs.TryGetValue(part.BuffGroupId, out List<BuffCfg>? pool) || pool.Count == 0)
			{
				continue;
			}
			BuffCfg buff = PickBuff(pool);
			uint data = buff.RollValue();
			uint packed = (partId & 0x3Fu) | ((buff.Id & 0xFFFu) << 6) | ((data & 0x3FFu) << 18);
			PlayerAttr attr = player.Attributes.Set(AttrGid, (uint)slot, packed);
			player.Attributes.SyncTo(sync, attr);
			result.Add((uint)(slot - PartBegin));
			Log($"  part slot={slot} idx={slot - PartBegin} part={partId} q={quality} type={type} sub={subType} buff={buff.Id} data={data}");
			slot = FindFreeSlot(player);
		}
		return result;
	}

	public static int FindFreeSlot(PlayerInstance player)
	{
		for (int sid = PartBegin; sid < PartEnd; sid++)
		{
			if (player.Attributes.GetValue(AttrGid, (uint)sid) == 0)
			{
				return sid;
			}
		}
		return -1;
	}

	private static int PickQuality(DropRow row)
	{
		if (row.Qualities.Count == 0)
		{
			return 1;
		}
		int total = row.Qualities.Sum((KeyValuePair<int, int> x) => Math.Max(0, x.Value));
		if (total <= 0)
		{
			return row.Qualities[0].Key;
		}
		int roll = Random.Shared.Next(total);
		foreach (KeyValuePair<int, int> item in row.Qualities)
		{
			if (item.Value <= 0)
			{
				continue;
			}
			roll -= item.Value;
			if (roll < 0)
			{
				return item.Key;
			}
		}
		return row.Qualities[0].Key;
	}

	public static BuffCfg PickBuff(List<BuffCfg> pool)
	{
		int total = pool.Sum((BuffCfg x) => Math.Max(0, x.Weight));
		if (total <= 0)
		{
			return pool[0];
		}
		int roll = Random.Shared.Next(total);
		foreach (BuffCfg item in pool)
		{
			if (item.Weight <= 0)
			{
				continue;
			}
			roll -= item.Weight;
			if (roll < 0)
			{
				return item;
			}
		}
		return pool[0];
	}

	/// <summary>
	/// 预留波次区间：返回本次应该从第几波开始发；返回 -1 表示这一波之前已经发过（重复包）。
	/// 同一次结算若重发（断线补偿 / 重复点击），last 已等于 waves，不会重复发奖。
	/// </summary>
	private static int ReserveWaves(PlayerInstance player, uint battleId, uint seed, int waves)
	{
		long scope = seed != 0 ? seed : -(long)battleId - 1;
		(long, long) key = (player.Uid, scope);
		lock (Gate)
		{
			int last = 0;
			if (Settled.TryGetValue(key, out int done))
			{
				last = done;
			}
			if (waves <= last)
			{
				return -1;
			}
			Settled[key] = waves;
			return last + 1;
		}
	}

	/// <summary>把 [G,D,P,L] 压成一个 key，用于同类金币累加。</summary>
	private static long CoinKey(uint[] coin)
	{
		return ((long)coin[0] << 24) | ((long)coin[1] << 16) | ((long)coin[2] << 8) | coin[3];
	}

	// ────────────────────────────── 奖励发放 ──────────────────────────────

	private static async ValueTask<bool> GrantOneAsync(PlayerInstance player, NtfSyncPlayer sync, uint[] row)
	{
		if (row.Length < 5 || row[0] == 0u || row[4] == 0u)
		{
			return false;
		}
		// 货币载体（cashbox）：4-1-23-9 战场经验、4-1-19-1 智略点 等，
		// 交给 AddCurrency 直接入账；丢给 RewardManager 会按 Param1*Count 放大。
		ulong key = GameResourceTemplateId.FromGdpl(row[0], row[1], row[2], row[3]);
		if (GameData.OtherItemByFullId.TryGetValue(key, out var item) && item != null && item.LuaType == "cashbox" && item.Param1 != 0)
		{
			player.AddCurrency(item.Param1, row[4], sync);
			Log($"  cash gdpl={row[0]},{row[1]},{row[2]},{row[3]} count={row[4]} -> cash {item.Param1}");
			return true;
		}
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>> { row };
		JsonArray granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		return granted.Count > 0;
	}

	private static JsonArray ToJsonArray(uint[] row)
	{
		JsonArray jsonArray = new JsonArray();
		for (int i = 0; i < 5; i++)
		{
			jsonArray.Add((int)(i < row.Length ? row[i] : 0u));
		}
		return jsonArray;
	}

	// ────────────────────────────── 配置加载 ──────────────────────────────

	private sealed class DropRow
	{
		public uint BattleId;

		public uint Waves;

		public int PartCount;

		public List<int> Types = new List<int>();

		public List<KeyValuePair<int, int>> Qualities = new List<KeyValuePair<int, int>>();

		public List<uint[]> Coins = new List<uint[]>();
	}

	public sealed class PartCfg
	{
		public uint Id;

		public int Quality;

		public int Type;

		public int SubType;

		public uint BuffGroupId;
	}

	public sealed class BuffCfg
	{
		public uint Id;

		public uint GroupId;

		public int Type;

		public int Weight;

		/// <summary>Type=2（技能型）的固定强度档；Type=1 不用这个字段。</summary>
		public uint Value;

		/// <summary>Type=1 的数值区间（来自 ValueRange，已钳到 10 位上限）。</summary>
		public int RangeMin;

		public int RangeMax;

		/// <summary>
		/// 每次发放都重新摇一次数值 —— 不能在配置加载时摇好存进 Value，
		/// 否则同一个词条在一整个进程生命周期里永远是同一个数（第 314 节修正）。
		/// </summary>
		public uint RollValue()
		{
			if (Type != 1)
			{
				return Value;
			}
			if (RangeMax <= RangeMin)
			{
				return (uint)Math.Clamp(RangeMin, 0, 1023);
			}
			return (uint)Random.Shared.Next(RangeMin, RangeMax + 1);
		}
	}

	private static List<DropRow>? Drops()
	{
		if (_drops != null)
		{
			return _drops;
		}
		List<DropRow> list = new List<DropRow>();
		try
		{
			string path = Res("dlc", "rogue3d", "score_model", "materialbuffdrop.json");
			if (File.Exists(path))
			{
				foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
				{
					if (!(node is JsonObject obj))
					{
						continue;
					}
					DropRow row = new DropRow
					{
						BattleId = ReadUint(obj["MaterialBuffBattleID"]),
						Waves = ReadUint(obj["Waves"])
					};
					if (obj["CoinReward"] is JsonArray coins)
					{
						foreach (JsonNode c in coins)
						{
							if (c is JsonArray arr && arr.Count >= 5)
							{
								row.Coins.Add(new uint[5]
								{
									ReadUint(arr[0]),
									ReadUint(arr[1]),
									ReadUint(arr[2]),
									ReadUint(arr[3]),
									ReadUint(arr[4])
								});
							}
						}
					}
					if (obj["ComponentReward"] is JsonArray comp && comp.Count >= 3)
					{
						row.PartCount = (int)ReadUint(comp[0]);
						if (comp[1] is JsonArray types)
						{
							foreach (JsonNode t in types)
							{
								int v = (int)ReadUint(t);
								if (v > 0)
								{
									row.Types.Add(v);
								}
							}
						}
						if (comp[2] is JsonArray weights)
						{
							foreach (JsonNode w in weights)
							{
								if (w is JsonArray pair && pair.Count >= 2)
								{
									row.Qualities.Add(new KeyValuePair<int, int>((int)ReadUint(pair[0]), (int)ReadUint(pair[1])));
								}
							}
						}
					}
					list.Add(row);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn("materialbuffdrop load failed: " + ex.Message);
		}
		_drops = list;
		return _drops;
	}

	private static DropRow? FindDrop(uint battleId, uint waves)
	{
		foreach (DropRow row in Drops() ?? new List<DropRow>())
		{
			if (row.BattleId == battleId && row.Waves == waves)
			{
				return row;
			}
		}
		return null;
	}

	private static Dictionary<long, Dictionary<uint, int>> BattleByLevelMap()
	{
		if (_battleByLevel != null)
		{
			return _battleByLevel;
		}
		Dictionary<long, Dictionary<uint, int>> map = new Dictionary<long, Dictionary<uint, int>>();
		try
		{
			string path = Res("dlc", "rogue3d", "score_model", "materialbuff_battle.json");
			if (File.Exists(path))
			{
				foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
				{
					if (!(node is JsonObject obj))
					{
						continue;
					}
					uint id = ReadUint(obj["ID"]);
					uint level = ReadUint(obj["Level"]);
					if (id != 0 && level != 0)
					{
						map[(long)level] = new Dictionary<uint, int> { [id] = 1 };
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn("materialbuff_battle load failed: " + ex.Message);
		}
		_battleByLevel = map;
		return _battleByLevel;
	}

	private static uint BattleByLevel(uint levelId)
	{
		if (levelId == 0)
		{
			return 0;
		}
		return BattleByLevelMap().TryGetValue(levelId, out Dictionary<uint, int>? hit) ? hit.Keys.First() : 0u;
	}

	private static int MaxWaves(uint battleId)
	{
		try
		{
			string path = Res("dlc", "rogue3d", "score_model", "materialbuff_battle.json");
			if (!File.Exists(path))
			{
				return 0;
			}
			foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
			{
				if (node is JsonObject obj && ReadUint(obj["ID"]) == battleId)
				{
					return (int)ReadUint(obj["Waves"]);
				}
			}
		}
		catch
		{
		}
		return 0;
	}

	public static Dictionary<uint, PartCfg>? Parts()
	{
		if (_parts != null)
		{
			return _parts;
		}
		Dictionary<uint, PartCfg> map = new Dictionary<uint, PartCfg>();
		try
		{
			string path = Res("dlc", "rogue3d", "uav", "uav_part.json");
			if (File.Exists(path))
			{
				foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
				{
					if (node is JsonObject obj)
					{
						uint id = ReadUint(obj["ID"]);
						if (id != 0)
						{
							map[id] = new PartCfg
							{
								Id = id,
								Quality = (int)ReadUint(obj["Quality"]),
								Type = (int)ReadUint(obj["Type"]),
								SubType = (int)ReadUint(obj["SubType"]),
								BuffGroupId = ReadUint(obj["BuffGroupId"])
							};
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn("uav_part load failed: " + ex.Message);
		}
		_parts = map;
		return _parts;
	}

	public static Dictionary<uint, List<BuffCfg>>? BuffsByGroup()
	{
		if (_buffsByGroup != null)
		{
			return _buffsByGroup;
		}
		Dictionary<uint, List<BuffCfg>> map = new Dictionary<uint, List<BuffCfg>>();
		try
		{
			string path = Res("dlc", "rogue3d", "uav", "uav_buff.json");
			if (File.Exists(path))
			{
				foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? new JsonArray())
				{
					if (!(node is JsonObject obj))
					{
						continue;
					}
					uint id = ReadUint(obj["ID"]);
					uint group = ReadUint(obj["GroupId"]);
					if (id == 0 || group == 0)
					{
						continue;
					}
					int type = (int)ReadUint(obj["Type"]);
					BuffCfg cfg = new BuffCfg
					{
						Id = id,
						GroupId = group,
						Type = type,
						Weight = (int)ReadUint(obj["Weight"]),
						Value = 0u
					};
					if (type == 1)
					{
						(int Min, int Max) range = ParseRange(obj["ValueRange"]?.ToString());
						cfg.RangeMin = range.Min;
						cfg.RangeMax = range.Max;
					}
					else
					{
						cfg.Value = ReadUint(obj["StrengthLevel"]) & 0x3FFu;
					}
					if (!map.TryGetValue(group, out List<BuffCfg>? list))
					{
						list = new List<BuffCfg>();
						map[group] = list;
					}
					list.Add(cfg);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn("uav_buff load failed: " + ex.Message);
		}
		_buffsByGroup = map;
		return _buffsByGroup;
	}

	/// <summary>{Attack={560,800}} → (560, 800)，两端都钳到 10 位上限 1023。</summary>
	public static (int Min, int Max) ParseRange(string? valueRange)
	{
		if (string.IsNullOrWhiteSpace(valueRange))
		{
			return (0, 0);
		}
		MatchCollection nums = Regex.Matches(valueRange, "-?\\d+");
		if (nums.Count < 2)
		{
			return (0, 0);
		}
		if (!int.TryParse(nums[0].Value, out int min) || !int.TryParse(nums[1].Value, out int max))
		{
			return (0, 0);
		}
		if (max < min)
		{
			(min, max) = (max, min);
		}
		return (Math.Clamp(min, 0, 1023), Math.Clamp(max, 0, 1023));
	}

	private static string Res(params string[] parts)
	{
		List<string> list = new List<string> { Directory.GetCurrentDirectory(), "Resources" };
		list.AddRange(parts);
		return Path.Combine(list.ToArray());
	}

	// ────────────────────────────── 杂项 ──────────────────────────────

	private static JsonObject SafeParse(string param)
	{
		try
		{
			return JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject ?? new JsonObject();
		}
		catch
		{
			return new JsonObject();
		}
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue value && value.TryGetValue<uint>(out uint number))
		{
			return number;
		}
		return uint.TryParse(node?.ToString(), out uint parsed) ? parsed : 0u;
	}

	private static string Trim(string? text, int max)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		return text.Length <= max ? text : text.Substring(0, max) + "...";
	}

	private static void Log(string message)
	{
		try
		{
			File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(), "Config", "Logs", "rogue3buff.log"),
				DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}
}
