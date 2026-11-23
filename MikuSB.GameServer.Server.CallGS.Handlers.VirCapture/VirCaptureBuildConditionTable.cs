using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 建筑解锁条件（Resources/dlc/vircapture/build/buildingcondition.json，14 条）。
///
/// 字段：ConditionID / Type / Object / Data[]。
/// Type 与客户端 VirCaptureCondition.tbCheckFun 的对应关系（第 499 节实测确认）：
///   1 → CheckAttribute (VircaptureBuildCondition.lua:59)  Object=建筑ID, Data[0]=属性序号, Data[1]=等级
///   2 → CheckMissionFinished (:69)                        Data[0]=任务ID（Object 无用，见下）
///   3 → CheckMapLevel (:81)                               Object=地图ID, Data[0]=等级
///   4 → CheckBuildLevel (:90)                             Object=建筑ID, Data[0]=等级
///
/// Type 2 的 Object 在本表里既出现空串（条件 3）又出现 1（条件 6），
/// 而真实任务 ID（102/116/209/216…）全部在 Data[0] 里 —— 所以 Object 不是任务 ID，取 Data[0]。
/// </summary>
internal sealed class VirCaptureConditionRow
{
	public uint Id { get; init; }

	public uint Type { get; init; }

	public uint Object { get; init; }

	public List<uint> Data { get; init; } = new List<uint>();

	public uint Arg(int index)
	{
		return (index >= 0 && index < Data.Count) ? Data[index] : 0u;
	}
}

internal static class VirCaptureBuildConditionTable
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildCond");

	private static readonly object _lock = new object();

	private static Dictionary<uint, VirCaptureConditionRow>? _rows;

	public static Dictionary<uint, VirCaptureConditionRow> All
	{
		get
		{
			lock (_lock)
			{
				if (_rows != null)
				{
					return _rows;
				}
				Dictionary<uint, VirCaptureConditionRow> map = new Dictionary<uint, VirCaptureConditionRow>();
				try
				{
					string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "vircapture", "build", "buildingcondition.json");
					if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray rows)
					{
						foreach (JsonNode? node in rows)
						{
							if (node is not JsonObject row)
							{
								continue;
							}
							uint id = ReadUint(row["ConditionID"]);
							if (id == 0)
							{
								continue;
							}
							map[id] = new VirCaptureConditionRow
							{
								Id = id,
								Type = ReadUint(row["Type"]),
								Object = ReadUint(row["Object"]),
								Data = ReadUintList(row["Data"])
							};
						}
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] buildingcondition.json load failed: " + ex.Message);
					map.Clear();
				}
				_rows = map;
				return _rows;
			}
		}
	}

	public static bool TryGet(uint id, out VirCaptureConditionRow row)
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
}
