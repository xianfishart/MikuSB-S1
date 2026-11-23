using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.DLC;
using MikuSB.Proto;
using MikuSB.Util;
using S1Admin;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 抵御玩法（版本活动主界面上「古宅异变」这类按钮）的开启检查。
///
/// 2026-11-16（第 363 节）：第 25 期点「古宅异变」，打开的界面里商店/任务按钮、
/// 地图池、敌方等级全是第 35 期的（崇福集市 / 水城观光 正是 TxtDLC35.BtnShop / BtnMission）。
/// 根因：客户端不传 nId，服务端一律读存档 Gid10/Sid0；而这个值历史上被兜底写成 49，
/// 49 恰好是 defend_act.json 里 DlcId=35 的那一行 → 第 25 期界面拿到 35 期的活动行。
///
/// 现在改为按「当前期数」在 defend_act.json 里挑对应行：同一期通常有两行（两周一轮换），
/// 取最近一次已开始的那行，等价于官方当前周的活动。只有该期确实没有抵御活动（表里没有 DlcId）
/// 时才保留存档旧值，避免把原本能开的活动关掉。
/// </summary>
[CallGSApi("DefendLogic_CheckOpenAct")]
public class DefendLogic_CheckOpenAct : ICallGSHandler
{
	private static readonly Logger Log = new Logger("Defend");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint nId = player.GetAttr(10u, 0u);
		uint saved = nId;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (doc.RootElement.TryGetProperty("nId", out var node) && node.TryGetInt32(out var requested) && requested > 0)
			{
				nId = (uint)requested;
			}
		}
		catch
		{
		}
		uint eraId = 0u;
		try
		{
			eraId = DlcEraState.PickDefaultEraId(player, DateTime.Now);
		}
		catch
		{
		}
		uint picked = DefendActTable.PickForEra(eraId, DateTime.Now);
		if (picked != 0)
		{
			nId = picked;
		}
		if (nId == 0)
		{
			nId = 49u;
		}
		SetAttr(player, 10u, 0u, nId, sync);
		Log.Info($"CheckOpenAct era={eraId} pick={picked} nId={nId} saved={saved}");
		await CallGSRouter.SendScript(connection, "DefendLogic_CheckOpenAct", "{\"bOpen\":true,\"nId\":" + nId + ",\"nBeginTime\":1767211200,\"nEndTime\":2145816000}", sync);
	}

	private static void SetAttr(PlayerInstance player, uint gid, uint sid, uint value, NtfSyncPlayer sync)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			playerAttr = new PlayerAttr
			{
				Gid = gid,
				Sid = sid,
				Val = value
			};
			player.Data.Attrs.Add(playerAttr);
		}
		else
		{
			playerAttr.Val = value;
		}
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}
}

/// <summary>
/// defend_act.json（抵御活动行）的期数索引。
/// 行里的 <c>DlcId</c> 是所属版本期号；<c>StartTime</c> 形如 <c>[202502100400]</c>，即 yyyyMMddHHmm。
/// </summary>
internal static class DefendActTable
{
	private static readonly Logger Log = new Logger("Defend");

	private static readonly object Gate = new object();

	private static string _stamp = "";

	private static List<(uint DlcId, uint Id, DateTime Start)> _rows = new List<(uint, uint, DateTime)>();

	/// <summary>按期号挑一行；没有该期的抵御活动行时返回 0。</summary>
	public static uint PickForEra(uint eraId, DateTime now)
	{
		if (eraId == 0 || DlcPin.IsDefault(eraId))
		{
			return 0u;
		}
		List<(uint DlcId, uint Id, DateTime Start)> rows = Rows().Where(x => x.DlcId == eraId && x.Id != 0).ToList();
		if (rows.Count == 0)
		{
			Log.Info($"PickForEra era={eraId} 无匹配行（表内共 {Rows().Count} 行）");
			return 0u;
		}
		// 已经开过的行里取最近一次（同期两行是两周轮换，取后一周即「当前周」）
		List<(uint DlcId, uint Id, DateTime Start)> started = rows
			.Where(x => x.Start == DateTime.MinValue || x.Start <= now)
			.OrderByDescending(x => x.Start)
			.ThenByDescending(x => x.Id)
			.ToList();
		if (started.Count > 0)
		{
			return started[0].Id;
		}
		// 全部还没开始（服务端把时间放宽了，正常不会走到这里）→ 取最早一行
		return rows.OrderBy(x => x.Start).First().Id;
	}

	/// <summary>
	/// 候选路径：框架给的资源根（最可靠）→ 进程工作目录 → 程序目录。
	/// 第 363 节踩过：只用 GetCurrentDirectory 时 pick 恒为 0，说明服务端进程的工作目录
	/// 并不一定是 S1 根目录，故按优先级逐个尝试并把实际命中的路径写进日志。
	/// </summary>
	private static IEnumerable<string> CandidatePaths()
	{
		string resourceRoot = null;
		try
		{
			resourceRoot = ConfigManager.Config.Path.ResourcePath;
		}
		catch
		{
		}
		if (!string.IsNullOrEmpty(resourceRoot))
		{
			yield return Path.Combine(resourceRoot, "challenge", "defend", "defend_act.json");
		}
		yield return Path.Combine(Directory.GetCurrentDirectory(), "Resources", "challenge", "defend", "defend_act.json");
		yield return Path.Combine(AppContext.BaseDirectory, "Resources", "challenge", "defend", "defend_act.json");
	}

	private static string ResolvePath()
	{
		foreach (string candidate in CandidatePaths())
		{
			try
			{
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			catch
			{
			}
		}
		return "";
	}

	private static List<(uint DlcId, uint Id, DateTime Start)> Rows()
	{
		string path = ResolvePath();
		if (path.Length == 0)
		{
			Log.Info("DefendActTable: defend_act.json 未找到（候选路径全部不存在）");
			return new List<(uint, uint, DateTime)>();
		}
		string stamp;
		try
		{
			FileInfo info = new FileInfo(path);
			stamp = info.LastWriteTimeUtc.Ticks + ":" + info.Length;
		}
		catch
		{
			stamp = "";
		}
		lock (Gate)
		{
			if (stamp.Length > 0 && stamp == _stamp)
			{
				return _rows;
			}
		}
		List<(uint, uint, DateTime)> rows = new List<(uint, uint, DateTime)>();
		try
		{
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
			foreach (JsonElement item in doc.RootElement.EnumerateArray())
			{
				// 注意：DlcId 在表里是「数字或空串」混合类型，直接用 TryGetUInt32 会在空串上抛
				// InvalidOperationException（不是返回 false），整个循环中断 → 表变成 0 行。
				// 第 363 节就是踩了这个坑（pick 恒为 0）。
				uint id = ReadUInt(item, "ID");
				uint dlc = ReadUInt(item, "DlcId");
				DateTime start = DateTime.MinValue;
				if (item.TryGetProperty("StartTime", out var startNode) && startNode.ValueKind == JsonValueKind.Array && startNode.GetArrayLength() > 0)
				{
					start = ParseStamp(startNode[0]);
				}
				if (id != 0)
				{
					rows.Add((dlc, id, start));
				}
			}
		}
		catch (Exception ex)
		{
			Log.Info($"DefendActTable 解析失败 path={path} err={ex.GetType().Name}:{ex.Message}");
		}
		lock (Gate)
		{
			_stamp = stamp;
			_rows = rows;
		}
		return rows;
	}

	/// <summary>数字或数字字符串都能读；其它类型（空串等）返回 0，不抛异常。</summary>
	private static uint ReadUInt(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var node))
		{
			return 0u;
		}
		if (node.ValueKind == JsonValueKind.Number)
		{
			return node.TryGetUInt32(out var number) ? number : 0u;
		}
		if (node.ValueKind == JsonValueKind.String)
		{
			return uint.TryParse(node.GetString(), out var parsed) ? parsed : 0u;
		}
		return 0u;
	}

	private static DateTime ParseStamp(JsonElement node)
	{
		long value = 0L;
		if (node.ValueKind == JsonValueKind.Number)
		{
			if (!node.TryGetInt64(out value))
			{
				return DateTime.MinValue;
			}
		}
		else if (node.ValueKind != JsonValueKind.String || !long.TryParse(node.GetString(), out value))
		{
			return DateTime.MinValue;
		}
		try
		{
			return new DateTime((int)(value / 100000000), (int)(value / 1000000 % 100), (int)(value / 10000 % 100), (int)(value / 100 % 100), (int)(value % 100), 0);
		}
		catch
		{
			return DateTime.MinValue;
		}
	}
}
