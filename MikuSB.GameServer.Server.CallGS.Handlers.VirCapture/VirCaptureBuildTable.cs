using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛建筑配置（Resources/dlc/vircapture/build/building.json）。
/// 客户端侧对应 PalConfig.tbBuildingCfg / VirCaptureBuild 的各张取数函数。
/// </summary>
internal sealed class VirCaptureBuildingRow
{
	public uint Id { get; init; }

	public string NameKey { get; init; } = string.Empty;

	public uint BuildType { get; init; }

	public uint MapId { get; init; }

	public uint Area { get; init; }

	/// <summary>BuildLevel：建造/升级到「第 n 级」需要的蓝图岛现金（下标 0 起，[0] 一般是 0）。</summary>
	public List<uint> BuildLevel { get; init; } = new List<uint>();

	/// <summary>OrginInCome：每级基础收入。</summary>
	public List<uint> OrginInCome { get; init; } = new List<uint>();

	/// <summary>OpeCost：每级营业消耗。</summary>
	public List<uint> OpeCost { get; init; } = new List<uint>();

	/// <summary>OrginNeed：每级的建造材料（编码含义未完全确认，暂不扣）。</summary>
	public List<List<uint>> OrginNeed { get; init; } = new List<List<uint>>();

	/// <summary>BuildBuff：建成后挂的增益 ID。</summary>
	public List<uint> BuildBuff { get; init; } = new List<uint>();

	/// <summary>Condition：解锁条件 ID（buildingcondition.json）。</summary>
	public List<uint> Condition { get; init; } = new List<uint>();

	/// <summary>BuildingProgressbar：建筑各等级对「建筑进度条」的贡献点（1 基按等级取），第 517 节。</summary>
	public List<uint> BuildingProgressbar { get; init; } = new List<uint>();

	/// <summary>
	/// 属性等级上限（building.json 的 nMaxAttrLevel，官服配置是 16）。
	/// 注意：客户端常量 BUILD_MAX_ATTR = 5 是「属性种类数」，不是等级上限，第 509 节踩过这个坑。
	/// </summary>
	public uint MaxAttrLevel { get; init; } = 16u;

	/// <summary>
	/// 三条建筑属性的升级消耗表（building.json 的 Attr1 / Attr2 / Attr3）。
	/// 这三张表在原配置里是 {"属性值/2": 蓝图币} 形态，键不是等级，但**按等级顺序排列**，
	/// 因此排序后第 N 个值 = 升到第 N 级的花费（第 1 个一定是 0）。
	/// 实测：豪华餐厅 1 级 独特性2→3 = 280、娱乐性2→3 = 600、服务性1→2 = 160，与表完全一致。
	/// </summary>
	public List<List<uint>> AttrCost { get; init; } = new List<List<uint>>();

	public uint MaxLevel => (uint)Math.Max(1, BuildLevel.Count);

	/// <summary>升到 targetLevel 需要的蓝图币；越界或没配置返回 0。</summary>
	public uint AttrCostToReach(uint attrIndex, uint targetLevel)
	{
		int tableIndex = (int)attrIndex - 1;
		if (tableIndex < 0 || tableIndex >= AttrCost.Count)
		{
			return 0u;
		}
		List<uint> table = AttrCost[tableIndex];
		int index = (int)targetLevel - 1;
		if (index < 0 || index >= table.Count)
		{
			return 0u;
		}
		return table[index];
	}

	public uint CostToReach(uint level)
	{
		int index = (int)level - 1;
		if (index < 0 || index >= BuildLevel.Count)
		{
			return 0u;
		}
		return BuildLevel[index];
	}

	public uint IncomeAt(uint level)
	{
		int index = (int)level - 1;
		if (index < 0 || index >= OrginInCome.Count)
		{
			return 0u;
		}
		return OrginInCome[index];
	}

	public uint OpeCostAt(uint level)
	{
		int index = (int)level - 1;
		if (index < 0 || index >= OpeCost.Count)
		{
			return 0u;
		}
		return OpeCost[index];
	}
}

internal static class VirCaptureBuildTable
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuild");

	private static readonly object _lock = new object();

	private static Dictionary<uint, VirCaptureBuildingRow>? _rows;

	public static Dictionary<uint, VirCaptureBuildingRow> All
	{
		get
		{
			lock (_lock)
			{
				if (_rows != null)
				{
					return _rows;
				}
				Dictionary<uint, VirCaptureBuildingRow> map = new Dictionary<uint, VirCaptureBuildingRow>();
				try
				{
					string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "vircapture", "build", "building.json");
					if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray rows)
					{
						foreach (JsonNode? node in rows)
						{
							if (node is not JsonObject row)
							{
								continue;
							}
							uint id = ReadUint(row["ID"]);
							if (id == 0)
							{
								continue;
							}
							map[id] = new VirCaptureBuildingRow
							{
								Id = id,
								NameKey = row["BuildName"]?.ToString() ?? string.Empty,
								BuildType = ReadUint(row["BuildType"]),
								MapId = ReadUint(row["MapID"]),
								Area = ReadUint(row["Area"]),
								BuildLevel = ReadUintList(row["BuildLevel"]),
								OrginInCome = ReadUintList(row["OrginInCome"]),
								OpeCost = ReadUintList(row["OpeCost"]),
								OrginNeed = ReadNested(row["OrginNeed"]),
								BuildBuff = ReadUintList(row["BuildBuff"]),
								Condition = ReadUintList(row["Condition"]),
							BuildingProgressbar = ReadUintList(row["BuildingProgressbar"]),
								MaxAttrLevel = ReadUintOr(row["nMaxAttrLevel"], 16u),
								AttrCost = new List<List<uint>>
								{
									ReadSortedValues(row["Attr1"]),
									ReadSortedValues(row["Attr2"]),
									ReadSortedValues(row["Attr3"])
								}
							};
						}
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] building.json load failed: " + ex.Message);
					map.Clear();
				}
				_rows = map;
				return _rows;
			}
		}
	}

	public static bool TryGet(uint id, out VirCaptureBuildingRow row)
	{
		if (id == 0)
		{
			row = null;
			return false;
		}
		return All.TryGetValue(id, out row);
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue value)
		{
			if (value.TryGetValue<uint>(out uint u))
			{
				return u;
			}
			if (value.TryGetValue<int>(out int i))
			{
				return (i < 0) ? 0u : (uint)i;
			}
		}
		return uint.TryParse(node?.ToString(), out uint parsed) ? parsed : 0u;
	}

	private static uint ReadUintOr(JsonNode? node, uint fallback)
	{
		uint value = ReadUint(node);
		return (value == 0u) ? fallback : value;
	}

	/// <summary>
	/// Attr1/Attr2/Attr3 是 {"键": 值} 对象，键是「属性值/2」这类数字字符串，不是等级下标。
	/// 但配置里按键升序排列，正好对应第 1..16 级，所以按键数值排序后逐个取值即可。
	/// </summary>
	private static List<uint> ReadSortedValues(JsonNode? node)
	{
		List<(uint Key, uint Value)> pairs = new List<(uint, uint)>();
		if (node is JsonObject obj)
		{
			foreach (KeyValuePair<string, JsonNode?> item in obj)
			{
				if (uint.TryParse(item.Key, out uint key))
				{
					pairs.Add((key, ReadUint(item.Value)));
				}
			}
		}
		else if (node is JsonArray array)
		{
			uint index = 0u;
			foreach (JsonNode? item in array)
			{
				pairs.Add((index++, ReadUint(item)));
			}
		}
		pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
		List<uint> values = new List<uint>(pairs.Count);
		foreach ((uint _, uint value) in pairs)
		{
			values.Add(value);
		}
		return values;
	}

	private static List<uint> ReadUintList(JsonNode? node)
	{
		List<uint> list = new List<uint>();
		if (node is JsonArray array)
		{
			foreach (JsonNode? item in array)
			{
				list.Add(ReadUint(item));
			}
		}
		return list;
	}

	private static List<List<uint>> ReadNested(JsonNode? node)
	{
		List<List<uint>> list = new List<List<uint>>();
		if (node is JsonArray array)
		{
			foreach (JsonNode? item in array)
			{
				list.Add(ReadUintList(item));
			}
		}
		return list;
	}
}
