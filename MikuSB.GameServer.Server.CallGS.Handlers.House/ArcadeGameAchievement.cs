using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 推币大赢家（ArcadeGame / CoinPusher）20 条成就的进度结算
/// （2026-11-13 新增，修复记录第 269 节）。
///
/// 病灶：ArcadeGameSettlement 只处理 tbConditionVal（单局目标）、tbPropUse（道具次数）、
/// 无尽最高分、少女回合数，从来没有把结算上报换算成成就进度；而客户端成就页读的是
/// `Achievement.GetProgres(GroupID, nId)` —— 反汇编 a_GetProgres / a_IsReceive 确认：
///   GetProgres = GetBits(me:GetAttribute(groupId, id), 1, 31)  → Val 的 bit1..31 就是进度
///   IsReceive  = GetBits(attr, 0, 0) == 1                      → Val 的 bit0 是已领标记
/// 也就是 Gid 7 / Sid = 成就ID 的同一份数据，与 HouseAchievementClaim.ClaimAsync 完全一致。
/// 服务端不写，进度就恒 0，成就页全灰、一键领取也领不到。
///
/// 口径来源：Resources/house/coinpusher/coinpusher_achv.json 的 AchType（388~396）
/// 与 Resources/achievement/activity_quest.json 的 CheckValue / Param / 文案：
///   388 (24001~24005) 和指定少女在普通模式完成 5 回合  → Param[0]=少女, Param[1]=目标回合
///   389 (24006~24008) 无尽模式达到 1000/5000/10000 分 → 101/18004 无尽最高分
///   390 (24009~24011) 任意模式连续推落 10/20/100 枚   → 结算 tbAchvParam 的连续数
///   391 (24012~24013) 图鉴全解锁（Param 1=守护道具 2=流派信物）
///   392 (24014)       堆起普通币塔                    → tbAchvParam 币塔计数
///   393 (24015)       炸弹币塔爆炸                    → tbAchvParam 炸弹币塔计数
///   394 (24016~24018) 累计推落 200/500/1000 枚        → tbAchvParam 累计推落计数
///   395 (24019)       一场对局放入 5 位不同少女的秘宝
///   396 (24020)       前 3 回合内让少女摇晃机台
///
/// tbAchvParam 的键名随版本可能变化，这里做别名兼容；未命中就不动，
/// 并把这局原始结算 JSON 落到 D:/DSH/.tools/arcade_settle.log 便于校准。
/// </summary>
internal static class ArcadeGameAchievement
{
	/// <summary>活动任务（含小游戏成就）的进度都在 Gid 7。</summary>
	private const uint QuestGid = 7u;

	/// <summary>普通模式少女回合数起始 Sid（与 ArcadeGameSettlement 一致）。</summary>
	private const uint GirlNormalRoundSidBase = 18010u;

	/// <summary>无尽模式最高分 Sid。</summary>
	private const uint EndlessScoreSid = 18004u;

	/// <summary>结算原始报文落盘位置，便于按真实字段校准。</summary>
	private const string DumpPath = "D:/DSH/.tools/arcade_settle.log";

	private const long DumpMaxBytes = 4194304L;

	private static List<Row>? _rows;

	private struct Row
	{
		public int Id;

		public int AchType;

		public int CheckValue;

		public List<int> Param;
	}

	/// <summary>结算时按上报数据把 20 条成就的进度写进 Gid 7（只增不减，保留已领位）。</summary>
	internal static void Apply(Connection connection, JsonObject root, NtfSyncPlayer sync)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		Dump(root);
		Dictionary<string, int> stat = CollectStat(root);
		bool dirty = false;
		foreach (Row row in LoadRows())
		{
			int progress;
			switch (row.AchType)
			{
			case 388:
				progress = ProgressGirlNormalRound(player, row);
				break;
			case 389:
				progress = Math.Min((int)HouseAttr.Read(player, EndlessScoreSid), row.CheckValue);
				break;
			case 390:
				progress = ProgressCombo(stat, row);
				break;
			case 392:
				progress = ProgressFlag(stat, row, "nCoinTowerNum", "nTowerNum", "nCoinTowerCount");
				break;
			case 393:
				progress = ProgressFlag(stat, row, "nBombCoinTowerNum", "nBombTowerNum", "nBombCoinTowerCount");
				break;
			case 394:
				progress = ProgressAccumulate(player, stat, row);
				break;
			case 395:
				progress = ProgressGirlGacha(root, row);
				break;
			case 396:
				progress = ProgressShakeInRound(stat, row);
				break;
			default:
				progress = -1;
				break;
			}
			if (progress > 0 && WriteProgress(player, sync, row.Id, progress))
			{
				dirty = true;
			}
		}
		if (dirty)
		{
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
	}

	/// <summary>388：普通模式与某位少女累计完成的回合数（Sid 18010+gid，已按局累加）。</summary>
	private static int ProgressGirlNormalRound(PlayerInstance player, Row row)
	{
		if (row.Param.Count < 2)
		{
			return -1;
		}
		int girlId = row.Param[0];
		int target = Math.Max(1, row.Param[1]);
		if (girlId <= 0)
		{
			return -1;
		}
		return Math.Min((int)HouseAttr.Read(player, GirlNormalRoundSidBase + (uint)girlId), target);
	}

	/// <summary>390：单次连续推落硬币数（各版本字段名不一，全部尝试）。</summary>
	private static int ProgressCombo(Dictionary<string, int> stat, Row row)
	{
		int value = Pick(stat, "nHit", "nMaxHit", "nMaxCombo", "nMaxContinuousNum", "nContinuousNum", "nCombo", "nMaxComboNum");
		if (value <= 0)
		{
			return -1;
		}
		return Math.Min(value, row.CheckValue);
	}

	/// <summary>392 / 393：一次性成就，计数出现过就算达成。</summary>
	private static int ProgressFlag(Dictionary<string, int> stat, Row row, params string[] names)
	{
		if (Pick(stat, names) <= 0)
		{
			return -1;
		}
		return row.CheckValue;
	}

	/// <summary>
	/// 394：累计推落硬币数。实测上报的 nCumulativeRemoveCoinNum 是本局统计（同包的 nHit /
	/// nCoinTowerNum / nUsePropNum 都是本局值），所以这里按"旧进度 + 本局"累加，
	/// 而不是取最大值，否则单局推落数一辈子也到不了 1000。
	/// </summary>
	private static int ProgressAccumulate(PlayerInstance player, Dictionary<string, int> stat, Row row)
	{
		int value = Pick(stat, "nCumulativeRemoveCoinNum", "nCumulativeCoinNum", "nRemoveCoinNum", "nRemovedCoinNum", "nTotalCoinNum", "nDropCoinNum");
		if (value <= 0)
		{
			return -1;
		}
		long old = (long)(ReadProgress(player, row.Id) >> 1) + value;
		return (int)Math.Min(old, row.CheckValue);
	}

	/// <summary>
	/// 395：一场对局里放过 5 位不同少女的扭蛋秘宝。
	/// 结算 tbPropUse 的元素是 [Type, GoodsID, Count]，其中 Type=2 / GoodsID 1~5 就是
	/// coinpusher_store.json 里 5 位少女对应的那组秘宝（Type=1 是 101~115 的普通道具）。
	/// </summary>
	private static int ProgressGirlGacha(JsonObject root, Row row)
	{
		if (!(root["tbPropUse"] is JsonArray propUses))
		{
			return -1;
		}
		HashSet<int> girls = new HashSet<int>();
		foreach (JsonNode node in propUses)
		{
			if (!(node is JsonArray item) || item.Count < 2)
			{
				continue;
			}
			if (HouseJson.ToInt(item[0]) != 2)
			{
				continue;
			}
			int goodsId = HouseJson.ToInt(item[1]);
			if (goodsId > 0)
			{
				girls.Add(goodsId);
			}
		}
		if (girls.Count < 5)
		{
			return -1;
		}
		return row.CheckValue;
	}

	/// <summary>396：前 3 回合内让少女摇晃机台（Param[0]=界限回合 3）。</summary>
	private static int ProgressShakeInRound(Dictionary<string, int> stat, Row row)
	{
		int limit = row.Param.Count > 0 ? Math.Max(1, row.Param[0]) : 3;
		int round = Pick(stat, "nUseLastRound", "nLastUseRound", "nShakeRound", "nLastShakeRound");
		if (round <= 0)
		{
			return -1;
		}
		if (round > limit)
		{
			return -1;
		}
		return row.CheckValue;
	}

	/// <summary>读某条成就当前的属性值（bit0 已领、bit1..31 进度）。</summary>
	private static uint ReadProgress(PlayerInstance player, int id)
	{
		return player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == QuestGid && x.Sid == (uint)id)?.Val ?? 0;
	}

	/// <summary>把进度写进 Gid 7（Val = 已领位 | 进度&lt;&lt;1），只增不减。</summary>
	private static bool WriteProgress(PlayerInstance player, NtfSyncPlayer sync, int id, int progress)
	{
		PlayerAttr? attr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == QuestGid && x.Sid == (uint)id);
		if (attr == null)
		{
			attr = new PlayerAttr
			{
				Gid = QuestGid,
				Sid = (uint)id,
				Val = 0u
			};
			player.Data.Attrs.Add(attr);
		}
		uint target = (uint)progress << 1;
		if ((attr.Val & 0xFFFFFFFEu) >= target)
		{
			return false;
		}
		attr.Val = (attr.Val & 1u) | target;
		sync.Custom[player.ToPackedAttrKey(QuestGid, (uint)id)] = attr.Val;
		sync.Custom[player.ToShiftedAttrKey(QuestGid, (uint)id)] = attr.Val;
		return true;
	}

	private static Dictionary<string, int> CollectStat(JsonObject root)
	{
		Dictionary<string, int> stat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (root["tbAchvParam"] is JsonObject achvParam)
		{
			foreach (KeyValuePair<string, JsonNode?> kv in achvParam)
			{
				stat[kv.Key] = Math.Max(0, HouseJson.ToInt(kv.Value));
			}
		}
		// 部分版本把计数器平铺在根节点上，一并收进来。
		foreach (string key in new string[13]
		{
			"nCoinTowerNum", "nBombCoinTowerNum", "nHit", "nMaxHit", "nMaxCombo", "nCumulativeRemoveCoinNum",
			"nUsePropNum", "nUseLastRound", "nGirlGachaNum", "nRemoveCoinNum", "nTotalCoinNum", "nMaxScore", "nFinishRound"
		})
		{
			if (!stat.ContainsKey(key))
			{
				int value = HouseJson.NumField(root, key);
				if (value > 0)
				{
					stat[key] = value;
				}
			}
		}
		return stat;
	}

	private static int Pick(Dictionary<string, int> stat, params string[] names)
	{
		foreach (string name in names)
		{
			if (stat.TryGetValue(name, out int value) && value > 0)
			{
				return value;
			}
		}
		return 0;
	}

	private static List<Row> LoadRows()
	{
		if (_rows != null)
		{
			return _rows;
		}
		List<Row> list = new List<Row>();
		try
		{
			Dictionary<int, int> achTypes = LoadAchTypes();
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "achievement", "activity_quest.json");
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray quests)
			{
				foreach (JsonNode node in quests)
				{
					if (!(node is JsonObject obj))
					{
						continue;
					}
					int id = HouseJson.NumField(obj, "ID");
					if (id < 24001 || id > 24020)
					{
						continue;
					}
					Row row = new Row
					{
						Id = id,
						CheckValue = Math.Max(1, HouseJson.NumField(obj, "CheckValue")),
						AchType = (achTypes.TryGetValue(id, out int achType) ? achType : HouseJson.NumField(obj, "Type")),
						Param = new List<int>()
					};
					if (obj["Param"] is JsonArray param)
					{
						foreach (JsonNode item in param)
						{
							row.Param.Add(HouseJson.ToInt(item));
						}
					}
					list.Add(row);
				}
			}
		}
		catch
		{
		}
		_rows = list;
		return list;
	}

	private static Dictionary<int, int> LoadAchTypes()
	{
		Dictionary<int, int> map = new Dictionary<int, int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "coinpusher", "coinpusher_achv.json");
			if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray list)
			{
				foreach (JsonNode node in list)
				{
					if (node is JsonObject obj)
					{
						int id = HouseJson.NumField(obj, "ID");
						if (id > 0)
						{
							map[id] = Math.Max(1, HouseJson.NumField(obj, "AchType"));
						}
					}
				}
			}
		}
		catch
		{
		}
		return map;
	}

	/// <summary>把本局结算报文追加到日志，超 4MB 先清空，避免长期运行撑爆磁盘。</summary>
	private static void Dump(JsonObject root)
	{
		try
		{
			FileInfo info = new FileInfo(DumpPath);
			if (info.Exists && info.Length > DumpMaxBytes)
			{
				File.WriteAllText(DumpPath, string.Empty);
			}
			string text = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + root.ToJsonString() + Environment.NewLine;
			File.AppendAllText(DumpPath, text);
		}
		catch
		{
		}
	}
}
