using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MikuSB.GameServer.Game.Dlc;

namespace S1Admin;

/// <summary>
/// 第 424 节：读某一期 DLC 的「功能活动 id 表」（dlc_activities.json 的 DlcFuncConfId 列）。
///
/// 客户端的 DLC 主窗口卡片是按 <c>DLC_Logic.tbActivityConf[期].tbDlcFuncConfId</c> 分的，
/// 例如第 34 期 <c>{Puzzle=417, Farmwar=2, Celebrate=1, Defense=34, Rush=103, Event=100}</c>。
/// 服务端过去只认 TryID / 时间窗口，遇到「同一类活动有多期」的场景（爆爆菜园 1/2 期、
/// 拼图 390/417）就会挑错行——第 34 期卡片点进去显示的是第 1 期爆爆菜园（早已结束）。
/// 这里把那张表解析出来，让服务端选择「本期该用哪一期活动」。
///
/// 只读文件并缓存，文件缺失/格式异常时返回 0（调用方按原逻辑兜底）。
/// </summary>
public static class DlcEraFunc
{
	private static readonly object Gate = new object();

	private static Dictionary<uint, Dictionary<string, uint>>? _cache;

	private static readonly Regex PairRegex = new Regex("([A-Za-z0-9_]+)\\s*=\\s*(\\d+)", RegexOptions.Compiled);

	public static uint Get(uint eraId, string key)
	{
		try
		{
			Dictionary<uint, Dictionary<string, uint>>? map = Load();
			if (map == null || !map.TryGetValue(eraId, out Dictionary<string, uint>? funcs))
			{
				return 0u;
			}
			return funcs.TryGetValue(key, out uint id) ? id : 0u;
		}
		catch
		{
			return 0u;
		}
	}

	/// <summary>当前钉住期的功能 id（没有钉期则返回 0）。</summary>
	public static uint Current(string key)
	{
		uint era = DlcPin.Override();
		return era == 0 ? 0u : Get(era, key);
	}

	private static Dictionary<uint, Dictionary<string, uint>>? Load()
	{
		lock (Gate)
		{
			if (_cache != null)
			{
				return _cache;
			}
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "dlc", "dlc_activities.json");
			if (!File.Exists(path))
			{
				return null;
			}
			JsonArray arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
			Dictionary<uint, Dictionary<string, uint>> map = new Dictionary<uint, Dictionary<string, uint>>();
			foreach (JsonNode? node in arr)
			{
				if (node is not JsonObject row)
				{
					continue;
				}
				if (!uint.TryParse(row["ID"]?.ToString(), out uint id))
				{
					continue;
				}
				string raw = row["DlcFuncConfId"]?.ToString() ?? "";
				Dictionary<string, uint> funcs = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
				foreach (Match m in PairRegex.Matches(raw))
				{
					if (uint.TryParse(m.Groups[2].Value, out uint fid))
					{
						funcs[m.Groups[1].Value] = fid;
					}
				}
				map[id] = funcs;
			}
			_cache = map;
			return map;
		}
	}
}
