using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「蓝图岛」任务表（Resources/dlc/vircapture/build/vircapture_task.json）。
///
/// 这张表和「开拓目标」（vircapture_mission.json / 属性组 133）是两套东西：
///   · 蓝图岛任务写在属性组 163（客户端 VirCaptureTask.GroupId = 163），
///   · 每行有 BigType（1 主线 / 2 支线 / 3 特殊 / 4 额外 / 5 剩余 / 6 随机）、
///     Type（18 对话、9 交互建造、2 建筑等级、12 蓝图岛等级…）、CheckValue、Param、PreID、Rewards。
/// 服务端原来整套逻辑都按 133 + vircapture_mission.json 走，蓝图岛这边一条都没写，
/// 所以客户端看到的任务列表永远是空的（主线 101 依赖特殊任务 100，100 又没人去完成它）。
/// </summary>
internal sealed class VirCaptureBuildTaskRow
{
	public uint Id { get; init; }

	public uint BigType { get; init; }

	public uint Type { get; init; }

	public uint CheckValue { get; init; } = 1u;

	public List<uint> Param { get; init; } = new List<uint>();

	public List<uint> PreId { get; init; } = new List<uint>();

	public string Name { get; init; } = string.Empty;

	public string Describe { get; init; } = string.Empty;

	/// <summary>Rewards 原文（可能是数组式 [1,4,1,6,...]，也可能是对象式 {"4":121}）。</summary>
	public JsonNode? Rewards { get; init; }

	/// <summary>没有文案、没有奖励的「引导占位任务」——官方是在进岛时直接算完成的那条。</summary>
	public bool IsPlaceholder => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(Describe) && (Rewards == null || Rewards.ToJsonString() == "{}");
}

internal static class VirCaptureBuildTaskTable
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureBuildTask");

	public const uint GroupId = 163u;

	private static Dictionary<uint, VirCaptureBuildTaskRow>? _rows;

	private static readonly object _lock = new object();

	private static readonly Dictionary<uint, VirCaptureBuildTaskRow> Empty = new Dictionary<uint, VirCaptureBuildTaskRow>();

	public static Dictionary<uint, VirCaptureBuildTaskRow> All
	{
		get
		{
			lock (_lock)
			{
				if (_rows != null)
				{
					return _rows;
				}
				Dictionary<uint, VirCaptureBuildTaskRow> map = new Dictionary<uint, VirCaptureBuildTaskRow>();
				try
				{
					string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "vircapture", "build", "vircapture_task.json");
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
							map[id] = new VirCaptureBuildTaskRow
							{
								Id = id,
								BigType = ReadUint(row["BigType"]),
								Type = ReadUint(row["Type"]),
								CheckValue = Math.Max(1u, ReadUint(row["CheckValue"])),
								Param = ReadUintList(row["Param"]),
								PreId = ReadUintList(row["PreID"]),
								Name = row["Name"]?.ToString() ?? string.Empty,
								Describe = row["Describe"]?.ToString() ?? string.Empty,
								Rewards = row["Rewards"]?.DeepClone()
							};
						}
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] build/vircapture_task.json load failed: " + ex.Message);
					map.Clear();
				}
				_rows = map;
				return _rows;
			}
		}
	}

	public static bool TryGet(uint id, out VirCaptureBuildTaskRow row)
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
