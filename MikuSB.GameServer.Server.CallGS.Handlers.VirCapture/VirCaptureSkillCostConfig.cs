using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 伙伴技能「升阶 / 解雇返还」配置（第 342 节）。
///
/// 数据来自 <c>Resources/dlc/vircapture/skillcost.json</c>（服务端没有对应的 Excel 类，
/// 这里直接读原文件，读不到时回落到内置的同值表）：
///   Id = CostId（skilllist.json 里 25 个技能的 CostId 全是 1）
///   Level1..Level6  → 从当前等级升到下一级要消耗的 GDPL+数量
///   Recycle1..Recycle6 → 解雇时按该技能等级返还的 GDPL+数量
///
/// 客户端的算法（uw_vircapture_skillup.ShowMain / ItemRecycle.GetMonsterRewards）：
///   升阶：cost = GetSkillCostConfig(skill.nCostId).tbLevelCost[当前等级]
///   解雇：对技能槽 4..6，各取 tbRecycle[该技能等级]，把条目依次拼起来
/// 服务端这里保持完全一致，避免界面预览和实际到账不一致。
/// </summary>
internal sealed class VirCaptureSkillCostConfig
{
	private const int MaxLevel = 6;

	private static readonly object Gate = new object();

	private static VirCaptureSkillCostConfig _instance;

	private readonly Dictionary<int, Dictionary<int, List<List<uint>>>> _levelCost;

	private readonly Dictionary<int, Dictionary<int, List<List<uint>>>> _recycle;

	private VirCaptureSkillCostConfig(Dictionary<int, Dictionary<int, List<List<uint>>>> levelCost, Dictionary<int, Dictionary<int, List<List<uint>>>> recycle)
	{
		_levelCost = levelCost;
		_recycle = recycle;
	}

	public static VirCaptureSkillCostConfig Instance
	{
		get
		{
			if (_instance != null)
			{
				return _instance;
			}
			lock (Gate)
			{
				return _instance ??= Load();
			}
		}
	}

	/// <summary>升到下一级的消耗；已满级返回空列表。</summary>
	public List<List<uint>> GetLevelCost(int costId, uint level)
	{
		return Lookup(_levelCost, costId, level);
	}

	/// <summary>按该等级解雇时返还的材料。</summary>
	public List<List<uint>> GetRecycle(int costId, uint level)
	{
		return Lookup(_recycle, costId, level);
	}

	private static List<List<uint>> Lookup(Dictionary<int, Dictionary<int, List<List<uint>>>> table, int costId, uint level)
	{
		if (level < 1u || level > MaxLevel)
		{
			return new List<List<uint>>();
		}
		if (table.TryGetValue(costId, out Dictionary<int, List<List<uint>>> byLevel)
			&& byLevel.TryGetValue((int)level, out List<List<uint>> rows) && rows != null)
		{
			return rows;
		}
		return new List<List<uint>>();
	}

	private static VirCaptureSkillCostConfig Load()
	{
		Dictionary<int, Dictionary<int, List<List<uint>>>> levelCost = null;
		Dictionary<int, Dictionary<int, List<List<uint>>>> recycle = null;
		foreach (string path in CandidatePaths())
		{
			try
			{
				if (!File.Exists(path))
				{
					continue;
				}
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
				if (doc.RootElement.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				levelCost = new Dictionary<int, Dictionary<int, List<List<uint>>>>();
				recycle = new Dictionary<int, Dictionary<int, List<List<uint>>>>();
				foreach (JsonElement row in doc.RootElement.EnumerateArray())
				{
					if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("Id", out JsonElement idEl))
					{
						continue;
					}
					int id = idEl.GetInt32();
					levelCost[id] = ReadRows(row, "Level");
					recycle[id] = ReadRows(row, "Recycle");
				}
				Log.Info($"[VirCapture] skillcost.json 已加载：{levelCost.Count} 条（{path}）");
				break;
			}
			catch (Exception e)
			{
				Log.Warn("[VirCapture] skillcost.json 读取失败：" + e.Message);
			}
		}
		if (levelCost == null || levelCost.Count == 0)
		{
			Log.Warn("[VirCapture] skillcost.json 未找到，使用内置默认表");
			levelCost = BuildDefaultLevel();
			recycle = BuildDefaultRecycle();
		}
		return new VirCaptureSkillCostConfig(levelCost, recycle);
	}

	private static Dictionary<int, List<List<uint>>> ReadRows(JsonElement row, string prefix)
	{
		Dictionary<int, List<List<uint>>> result = new Dictionary<int, List<List<uint>>>();
		for (int lv = 1; lv <= MaxLevel; lv++)
		{
			if (!row.TryGetProperty(prefix + lv, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			List<List<uint>> rows = new List<List<uint>>();
			foreach (JsonElement one in arr.EnumerateArray())
			{
				if (one.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				List<uint> gdpl = new List<uint>(5);
				foreach (JsonElement v in one.EnumerateArray())
				{
					gdpl.Add(v.ValueKind == JsonValueKind.Number ? v.GetUInt32() : 0u);
				}
				if (gdpl.Count >= 5)
				{
					rows.Add(gdpl);
				}
			}
			if (rows.Count > 0)
			{
				result[lv] = rows;
			}
		}
		return result;
	}

	private static IEnumerable<string> CandidatePaths()
	{
		yield return Path.Combine(ConfigManager.Config.Path.ResourcePath, "dlc", "vircapture", "skillcost.json");
		yield return Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "vircapture", "skillcost.json");
		yield return Path.Combine(AppContext.BaseDirectory, "Resources", "dlc", "vircapture", "skillcost.json");
	}

	private static Dictionary<int, Dictionary<int, List<List<uint>>>> BuildDefaultLevel()
	{
		return new Dictionary<int, Dictionary<int, List<List<uint>>>>
		{
			[1] = new Dictionary<int, List<List<uint>>>
			{
				[1] = Rows("5,25,2,2,50"),
				[2] = Rows("5,25,2,2,50", "5,25,2,3,15"),
				[3] = Rows("5,25,2,2,100", "5,25,2,3,30", "5,25,2,4,6"),
				[4] = Rows("5,25,2,2,100", "5,25,2,3,30", "5,25,2,4,6"),
				[5] = Rows("5,25,2,4,6", "5,25,2,5,3")
			}
		};
	}

	private static Dictionary<int, Dictionary<int, List<List<uint>>>> BuildDefaultRecycle()
	{
		return new Dictionary<int, Dictionary<int, List<List<uint>>>>
		{
			[1] = new Dictionary<int, List<List<uint>>>
			{
				[2] = Rows("5,25,2,2,10"),
				[3] = Rows("5,25,2,2,20", "5,25,2,3,3"),
				[4] = Rows("5,25,2,2,40", "5,25,2,3,9", "5,25,2,4,1"),
				[5] = Rows("5,25,2,2,60", "5,25,2,3,15", "5,25,2,4,2"),
				[6] = Rows("5,25,2,2,60", "5,25,2,3,15", "5,25,2,4,3")
			}
		};
	}

	private static List<List<uint>> Rows(params string[] specs)
	{
		List<List<uint>> rows = new List<List<uint>>(specs.Length);
		foreach (string spec in specs)
		{
			string[] parts = spec.Split(',');
			List<uint> gdpl = new List<uint>(parts.Length);
			foreach (string part in parts)
			{
				gdpl.Add(uint.Parse(part.Trim()));
			}
			rows.Add(gdpl);
		}
		return rows;
	}

	private static readonly Logger Log = new Logger("VirCapture.Skill");
}
