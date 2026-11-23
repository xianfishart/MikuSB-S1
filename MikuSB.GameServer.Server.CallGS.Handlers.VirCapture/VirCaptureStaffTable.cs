using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 蓝图岛员工配置（Resources/dlc/vircapture/build/vircapture_staff.json）。
/// 客户端侧对应 PalConfig.tbStaffCfg。
/// </summary>
internal sealed class VirCaptureStaffRow
{
	public uint Id { get; init; }

	public string Name { get; init; } = string.Empty;

	public uint Quality { get; init; }

	/// <summary>初始等级（正常都是 1）——招到时写进 162/Level 的就是它。</summary>
	public uint Level { get; init; } = 1u;

	public uint MaxLevel { get; init; } = 4u;

	/// <summary>升级花费（Consume），本服还没做员工升级，先存着。</summary>
	public List<uint> Consume { get; init; } = new List<uint>();

	/// <summary>StaffProgressbar：员工各等级对「员工进度条」的贡献点（1 基按等级取），第 517 节。</summary>
	public List<uint> StaffProgressbar { get; init; } = new List<uint>();
}

/// <summary>
/// 招聘池一行（recruitment_staff.json）。
/// StaffID = 0 表示「该档位随机出一名」的占位行。
/// </summary>
internal sealed class VirCaptureRecruitRow
{
	public uint MapId { get; init; }

	public uint StaffId { get; init; }

	/// <summary>Allor：官方语义未知，全是 1，原样保留。</summary>
	public uint AllOr { get; init; }

	/// <summary>Finishtaskid：要求该任务**已领奖**才出现在招募列表里（0 = 无要求）。</summary>
	public uint FinishTaskId { get; init; }

	/// <summary>Money：招募花费，货币是 operating_data.json 的 Cashid（蓝图岛现金）。</summary>
	public uint Money { get; init; }

	/// <summary>Num：额外数量条件（[当前值, 目标值]），本服暂未使用。</summary>
	public List<uint> Num { get; init; } = new List<uint>();
}

/// <summary>
/// 员工 / 招募 / 经营货币三张表。
///
/// 属性组与位域（从客户端模块常量直接读出，第 498 节确认，不再是推测）：
///   VirCaptureStaff.GroupId        = 162
///   VirCaptureStaff.StaffDataStart = 100
///   VirCaptureStaff.StaffDataCount = 20
///   VirCaptureStaff.StaffTaskOffset = { Level = 1, Flag = 2, WorkBuilding = 3 }
/// 客户端 GetStaffOffData(id, off) 的写法是：
///   key = StaffDataStart + id * StaffDataCount + off   （越界返回 0）
///   → GetStaffData(key) = me:GetAttribute(162, key)
/// </summary>
internal static class VirCaptureStaffTable
{
	private static readonly MikuSB.Util.Logger Log = new MikuSB.Util.Logger("VirCaptureStaff");

	public const uint GroupId = 162u;

	public const uint DataStart = 100u;

	public const uint DataCount = 20u;

	public const uint OffLevel = 1u;

	public const uint OffFlag = 2u;

	public const uint OffWorkBuilding = 3u;

	private static readonly object _lock = new object();

	private static Dictionary<uint, VirCaptureStaffRow>? _staff;

	private static List<VirCaptureRecruitRow>? _recruit;

	private static Dictionary<uint, uint>? _cashByMap;

	/// <summary>operating_data.json 的 StaffProgressbar（员工进度条 5 档阈值，326 = [90,160,240,340,340]）。</summary>
	private static Dictionary<uint, List<uint>>? _staffProgByMap;

	/// <summary>operating_data.json 的 BuildingProgressbar（建筑进度条 5 档阈值，326 = [300,400,700,1100,1100]）。</summary>
	private static Dictionary<uint, List<uint>>? _buildProgByMap;

	/// <summary>员工位域 key：162/(100 + id*20 + off)。</summary>
	public static uint Key(uint staffId, uint offset)
	{
		return DataStart + staffId * DataCount + offset;
	}

	public static Dictionary<uint, VirCaptureStaffRow> Staff
	{
		get
		{
			lock (_lock)
			{
				if (_staff != null)
				{
					return _staff;
				}
				Dictionary<uint, VirCaptureStaffRow> map = new Dictionary<uint, VirCaptureStaffRow>();
				try
				{
					foreach (JsonObject row in ReadArray("vircapture_staff.json"))
					{
						uint id = ReadUint(row["Id"]);
						if (id == 0)
						{
							continue;
						}
						map[id] = new VirCaptureStaffRow
						{
							Id = id,
							Name = row["Name"]?.ToString() ?? string.Empty,
							Quality = ReadUint(row["Quality"]),
							Level = Math.Max(1u, ReadUint(row["Level"])),
							MaxLevel = Math.Max(1u, ReadUint(row["MaxLevel"])),
							Consume = ReadUintList(row["Consume"]),
							StaffProgressbar = ReadUintList(row["StaffProgressbar"])
						};
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] vircapture_staff.json load failed: " + ex.Message);
					map.Clear();
				}
				_staff = map;
				return _staff;
			}
		}
	}

	public static List<VirCaptureRecruitRow> Recruit
	{
		get
		{
			lock (_lock)
			{
				if (_recruit != null)
				{
					return _recruit;
				}
				List<VirCaptureRecruitRow> list = new List<VirCaptureRecruitRow>();
				try
				{
					foreach (JsonObject row in ReadArray("recruitment_staff.json"))
					{
						list.Add(new VirCaptureRecruitRow
						{
							MapId = ReadUint(row["Mapid"]),
							StaffId = ReadUint(row["StaffID"]),
							AllOr = ReadUint(row["Allor"]),
							FinishTaskId = ReadUint(row["Finishtaskid"]),
							Money = ReadUint(row["Money"]),
							Num = ReadUintList(row["Num"])
						});
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] recruitment_staff.json load failed: " + ex.Message);
					list.Clear();
				}
				_recruit = list;
				return _recruit;
			}
		}
	}

	/// <summary>蓝图岛现金货币 ID（operating_data.json 的 Cashid，326 = 55）。</summary>
	public static uint CashId(uint mapId)
	{
		lock (_lock)
		{
			if (_cashByMap == null)
			{
				Dictionary<uint, uint> map = new Dictionary<uint, uint>();
				try
				{
					foreach (JsonObject row in ReadArray("operating_data.json"))
					{
						uint id = ReadUint(row["Mapid"]);
						if (id != 0)
						{
							map[id] = ReadUint(row["Cashid"]);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] operating_data.json load failed: " + ex.Message);
				}
				_cashByMap = map;
			}
			return _cashByMap.GetValueOrDefault(mapId);
		}
	}

	/// <summary>
	/// 经营效果档位阈值（operating_data.json，第 517 节）。
	/// StaffProgressbar = 员工侧 5 档、BuildingProgressbar = 建筑侧 5 档；
	/// 客户端 tbOperateDataList 里这两列没被 LoadOperateDataConfig 解析（运行时探针确认），
	/// 只有原始 CSV/JSON 里有，按原始文件直接读。
	/// </summary>
	public static void GetEffectThresholds(uint mapId, out List<uint> staffThresholds, out List<uint> buildThresholds)
	{
		lock (_lock)
		{
			if (_staffProgByMap == null || _buildProgByMap == null)
			{
				Dictionary<uint, List<uint>> staffMap = new Dictionary<uint, List<uint>>();
				Dictionary<uint, List<uint>> buildMap = new Dictionary<uint, List<uint>>();
				try
				{
					foreach (JsonObject row in ReadArray("operating_data.json"))
					{
						uint id = ReadUint(row["Mapid"]);
						if (id == 0)
						{
							continue;
						}
						staffMap[id] = ReadUintList(row["StaffProgressbar"]);
						buildMap[id] = ReadUintList(row["BuildingProgressbar"]);
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[VirCapture] operating_data.json progressbar load failed: " + ex.Message);
				}
				_staffProgByMap = staffMap;
				_buildProgByMap = buildMap;
			}
			staffThresholds = _staffProgByMap.GetValueOrDefault(mapId) ?? new List<uint>();
			buildThresholds = _buildProgByMap.GetValueOrDefault(mapId) ?? new List<uint>();
		}
	}

	public static bool TryGetStaff(uint staffId, out VirCaptureStaffRow row)
	{
		if (staffId == 0)
		{
			row = null;
			return false;
		}
		return Staff.TryGetValue(staffId, out row);
	}

	public static bool TryGetRecruit(uint mapId, uint staffId, out VirCaptureRecruitRow row)
	{
		foreach (VirCaptureRecruitRow item in Recruit)
		{
			if (item.MapId == mapId && item.StaffId == staffId && staffId != 0)
			{
				row = item;
				return true;
			}
		}
		row = null;
		return false;
	}

	private static IEnumerable<JsonObject> ReadArray(string fileName)
	{
		string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "vircapture", "build", fileName);
		if (!File.Exists(path))
		{
			yield break;
		}
		if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray rows)
		{
			yield break;
		}
		foreach (JsonNode? node in rows)
		{
			if (node is JsonObject row)
			{
				yield return row;
			}
		}
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
