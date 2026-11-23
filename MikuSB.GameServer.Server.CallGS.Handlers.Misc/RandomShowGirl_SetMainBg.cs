using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

/// <summary>
/// 大厅「随机封面互动设置」保存（客户端 api：RandomShowGirl_SetMainBg）。
///
/// 请求实测形如：{"b3D":true,"tbId":[21007,21010,21011,21012]}
///   b3D  = true → 3D 互动情景池；false → 2D 插画池
///   tbId = 面板上勾选的场景 id 列表（顺序即面板顺序）
///
/// 存档布局（2026-11-15 第 350 节按官服客户端行为重新核对）：
///   139/401~414  = 3D 互动情景候选池，**14 格**（对应 scene_change.txt 里的 21001~21014）
///   139/1000~    = 2D 插画候选池，格数 = 插画总数（实测 1000~1150 共 151 格）
///   139/901      = 模式开关：0 = 3D，1 = 2D
///   132/1        = 当前场景（其它 UI 读这一格）
/// 登录时客户端在对应池子里随机挑一个非空槽位当大厅场景。
///
/// 第 350 节修的两个坑：
///   ① 以前 3D 只写 401..401+N-1，**不清后面**。老存档 405~413 被第 140 节的
///     「固定场景锁死 13 格」写成了 2（世界树营地），414 又是更早全选留下的 21014，
///     结果池子 = [4 个新选的] + [9 个 2] + [1 个 21014]，重登随机几乎必然落在 2 上，
///     面板里也只剩一格能认出来 —— 就是「点开只选定一个 3D」的真相。
///   ② 3D 池的格数是 14 不是 13（414 那一格确实被写过 21014）。
/// </summary>
[CallGSApi("RandomShowGirl_SetMainBg")]
public class RandomShowGirl_SetMainBg : ICallGSHandler
{
	private const uint AttrGroup = 139u;

	/// <summary>3D 互动情景池首格。</summary>
	private const uint Slot3DFirst = 401u;

	/// <summary>3D 互动情景池格数（21001~21014 共 14 个）。</summary>
	private const uint Slot3DCount = 14u;

	/// <summary>2D 插画池首格。</summary>
	private const uint Slot2DFirst = 1000u;

	private const uint MainSceneGid = 132u;

	private const uint MainSceneSid = 1u;

	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		List<int> list = new List<int>();
		bool flag = true;
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(param);
			if (jsonDocument.RootElement.TryGetProperty("b3D", out var value))
			{
				flag = value.ValueKind != JsonValueKind.False;
			}
			if (jsonDocument.RootElement.TryGetProperty("tbId", out var value2) && value2.ValueKind == JsonValueKind.Array)
			{
				list.AddRange(from x in value2.EnumerateArray()
					select x.TryGetInt32(out var value3) ? value3 : 0);
			}
		}
		catch
		{
		}
		// 去 0 去重（0 = 空槽，绝不能写进池子）
		list = list.Where((int x) => x > 0).Distinct().ToList();
		uint first = (flag ? Slot3DFirst : Slot2DFirst);
		uint cap = (flag ? Slot3DCount : 512u);
		if (list.Count > cap)
		{
			list = list.Take((int)cap).ToList();
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		for (int i = 0; i < list.Count; i++)
		{
			SetAttr(player, sync, first + (uint)i, (uint)list[i]);
		}
		// 池子严格等于本次勾选：把 N 格之后的旧槽位全部置空。
		// 3D 只清 401~414 这 14 格；2D 清 1000 之后的所有旧槽位（老存档有 151 格）。
		uint keepUntil = first + (uint)list.Count;
		uint clearTo = (flag ? (Slot3DFirst + Slot3DCount) : uint.MaxValue);
		List<uint> stale = player.Data.Attrs
			.Where((PlayerAttr x) => x.Gid == AttrGroup && x.Sid >= keepUntil && x.Sid < clearTo)
			.Select((PlayerAttr x) => x.Sid)
			.Distinct()
			.ToList();
		foreach (uint sid in stale)
		{
			SetAttr(player, sync, sid, 0u);
		}
		// 901：0 = 3D，1 = 2D
		SetAttr(player, sync, 901u, (!flag) ? 1u : 0u);
		// 3D 池非空时把「当前场景」对齐到勾选列表的第一张，
		// 免得 132/1 还停在旧值（例如 2 世界树营地）被别的界面读去。
		if (flag && list.Count > 0)
		{
			SetAttrGroup(player, sync, MainSceneGid, MainSceneSid, (uint)list[0]);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.CompletedTask;
	}

	private static void SetAttr(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
	{
		SetAttrGroup(player, sync, AttrGroup, sid, value);
	}

	private static void SetAttrGroup(PlayerInstance player, NtfSyncPlayer sync, uint gid, uint sid, uint value)
	{
		PlayerAttr playerAttr = player.Data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr == null)
		{
			if (value == 0)
			{
				return;
			}
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
