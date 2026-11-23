using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

/// <summary>
/// 部件精制（4 个同品质 → 1 个更高品质）。
///
/// 第 314 节：原实现只把 4 个槽清空、把「第一个部件的 partId」原样写回新槽，
/// 品质和词条都没动 —— 玩家看到的只是「4 个变 1 个」，等级没升。
///
/// 客户端协议（Rogue3DUav.ReqMerge / Rsp_OnMerge 反汇编确认）：
///   me:CallGS("Rogue3DUav_Merge", json.encode({ idx0, idx1, idx2, idx3 }))
///     参数是 4 个「部件相对下标」（0..259，对应属性 sid = 3105 + idx）
///     客户端自己先校验：数量 4、品质一致、未装配
///   回包：{ nIdx = 新部件下标 } 或 { sErrTip = 语言键 }
///     客户端拿 nIdx 走 Rogue3DUav.GetUavPart(nIdx) 展示并弹 Rogue32GainTip
///
/// 部件 = 属性组 124 的位打包值（同第 312 节）：
///   bit0..5 部件ID / bit6..17 词条ID / bit18..27 数值 / bit28..31 装配槽
/// 部件表 uav_part.json：ID = (Type-1)*9 + (3-Quality)*3 + SubType，共 36 条。
/// 品质 1/2/3 → BuffGroupId 分别 101/102/103（品质越高词条池越强）。
/// </summary>
[CallGSApi("Rogue3DUav_Merge")]
public class Rogue3DUav_Merge : ICallGSHandler
{
	private const uint AttrGid = 124u;

	private const int PartBegin = 3105;

	private const int PartEnd = 3365;

	private const int MaxQuality = 3;

	private static readonly Logger Logger = new Logger("Rogue3DUav");

	private sealed class Picked
	{
		public uint Sid;

		public uint Idx;

		public Rogue3DScoreModel_BuffLevelSettlement.PartCfg Part = null!;

		public uint DataId;

		public uint Data;
	}

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		List<uint> list = new List<uint>();
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "[]" : param);
			if (jsonNode is JsonArray arr)
			{
				foreach (JsonNode node in arr)
				{
					list.Add(Rogue3DAttr.ReadUInt(node));
				}
			}
		}
		catch
		{
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerInstance player = connection.Player;
		JsonObject rsp = new JsonObject();
		if (player == null)
		{
			await CallGSRouter.SendScript(connection, "Rogue3DUav_Merge", rsp.ToJsonString(), sync);
			return;
		}
		if (list.Count != 4 || list.Distinct().Count() != 4)
		{
			rsp["sErrTip"] = "rogue3.massage_UavErrorTips7";
			await CallGSRouter.SendScript(connection, "Rogue3DUav_Merge", rsp.ToJsonString(), sync);
			return;
		}
		Dictionary<uint, Rogue3DScoreModel_BuffLevelSettlement.PartCfg>? parts = Rogue3DScoreModel_BuffLevelSettlement.Parts();
		Dictionary<uint, List<Rogue3DScoreModel_BuffLevelSettlement.BuffCfg>>? buffs = Rogue3DScoreModel_BuffLevelSettlement.BuffsByGroup();
		if (parts == null || parts.Count == 0 || buffs == null)
		{
			rsp["sErrTip"] = "rogue3.massage_UavErrorTips3";
			await CallGSRouter.SendScript(connection, "Rogue3DUav_Merge", rsp.ToJsonString(), sync);
			return;
		}

		List<Picked> picked = new List<Picked>();
		string? fail = null;
		foreach (uint idx in list)
		{
			if (idx >= 260)
			{
				fail = "rogue3.massage_UavErrorTips3";
				break;
			}
			uint sid = (uint)(PartBegin + idx);
			uint v = Rogue3DAttr.Read(player, sid);
			if (v == 0)
			{
				fail = "rogue3.massage_UavErrorTips3";
				break;
			}
			// bit28..31 是装配槽，非 0 说明已装配在机体上，不能拿来精制。
			if (Rogue3DAttr.GetField(v, 28, 4) != 0)
			{
				fail = "rogue3.massage_UavErrorTips6";
				break;
			}
			uint partId = Rogue3DAttr.GetField(v, 0, 6);
			if (!parts.TryGetValue(partId, out Rogue3DScoreModel_BuffLevelSettlement.PartCfg? cfg) || cfg == null)
			{
				fail = "rogue3.massage_UavErrorTips3";
				break;
			}
			picked.Add(new Picked
			{
				Sid = sid,
				Idx = idx,
				Part = cfg,
				DataId = Rogue3DAttr.GetField(v, 6, 12),
				Data = Rogue3DAttr.GetField(v, 18, 10)
			});
		}
		if (fail == null)
		{
			int quality = picked[0].Part.Quality;
			if (picked.Any((Picked p) => p.Part.Quality != quality))
			{
				fail = "rogue3.massage_UavPartComposeQualityDiscrepancy";
			}
		}
		if (fail == null)
		{
			int quality = picked[0].Part.Quality;
			int newQuality = Math.Min(MaxQuality, quality + 1);
			// 精制后保留哪一「种」部件：4 件里随机取一件当模板（客户端只要求品质一致，
			// 4 件的 Type/SubType 可以不同），再把它的品质抬一级。
			Picked basis = picked[Random.Shared.Next(picked.Count)];
			int type = basis.Part.Type;
			int subType = basis.Part.SubType;
			Rogue3DScoreModel_BuffLevelSettlement.PartCfg? target = parts.Values.FirstOrDefault(
				(Rogue3DScoreModel_BuffLevelSettlement.PartCfg p) => p.Type == type && p.SubType == subType && p.Quality == newQuality);
			if (target == null)
			{
				fail = "rogue3.massage_UavErrorTips3";
			}
			else if (!buffs.TryGetValue(target.BuffGroupId, out List<Rogue3DScoreModel_BuffLevelSettlement.BuffCfg>? pool) || pool == null || pool.Count == 0)
			{
				fail = "rogue3.massage_UavErrorTips3";
			}
			else
			{
				Rogue3DScoreModel_BuffLevelSettlement.BuffCfg buff = Rogue3DScoreModel_BuffLevelSettlement.PickBuff(pool);
				uint data = buff.RollValue();
				uint packed = (target.Id & 0x3Fu) | ((buff.Id & 0xFFFu) << 6) | ((data & 0x3FFu) << 18);
				// 新部件落在 4 个槽里下标最小的那一格，其余清空 ——
				// 这样界面刷新后 4 合 1 的位置是稳定的，不会跳到列表最前面。
				picked.Sort((Picked a, Picked b) => a.Sid.CompareTo(b.Sid));
				uint keep = picked[0].Sid;
				Rogue3DAttr.Write(player, keep, packed, sync);
				for (int i = 1; i < picked.Count; i++)
				{
					Rogue3DAttr.Write(player, picked[i].Sid, 0u, sync);
				}
				rsp["nIdx"] = keep - PartBegin;
				DatabaseHelper.SaveDatabaseType(player.Data);
				Logger.Info($"[Rogue3DUav] merge uid={player.Uid} {quality}->{newQuality} " +
					$"part={target.Id} buff={buff.Id} data={data} keep={keep} src=[{string.Join(",", list)}]");
			}
		}
		if (rsp["nIdx"] == null)
		{
			rsp["sErrTip"] = fail ?? "rogue3.massage_UavErrorTips3";
			Logger.Info($"[Rogue3DUav] merge rejected uid={player.Uid} reason={rsp["sErrTip"]} src=[{string.Join(",", list)}]");
		}
		await CallGSRouter.SendScript(connection, "Rogue3DUav_Merge", rsp.ToJsonString(), sync);
	}
}
