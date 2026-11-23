using System;
using System.Collections.Generic;
using System.Linq;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

/// <summary>
/// 验证战场「部件」直发工具（第 315 节）。
///
/// 部件不是物品，是属性组 124 的位打包值（同第 312 节）：
///   sid = 3105 + idx（3105~3364，共 260 槽）
///   bit0..5 部件ID / bit6..17 词条ID / bit18..27 数值 / bit28..31 装配槽
///
/// 用途：测试精制 / 装配 / 词条时不必真去打增益试炼。GM 命令 /r3parts 与
/// 登录一次性补发（MailService.EnsureRogue3DebugParts）都走这里，保证口径一致。
/// 发放顺序：同一品质内先把这个品质的 12 种（Type 1~4 × SubType 1~3）各铺一遍，
/// 多出来的按种类轮转；词条与数值每次重新摇。
/// </summary>
public static class Rogue3DUavGrant
{
	private const uint AttrGid = 124u;

	public const uint UavIdSid = 3101u;

	public const int PartBegin = 3105;

	public const int PartEnd = 3365;

	/// <summary>「一次性补发已执行」标记，放在部件区之外的 3499，客户端不会遍历到这里。</summary>
	public const uint GrantedFlagSid = 3499u;

	public sealed class GrantResult
	{
		public List<(int Quality, int Count)> ByQuality = new List<(int, int)>();

		public int Total;

		public bool SlotsFull;

		public int SlotsUsed;
	}

	public static bool AlreadyGranted(PlayerInstance player)
	{
		return player.Attributes.GetValue(AttrGid, GrantedFlagSid) != 0;
	}

	public static void MarkGranted(PlayerInstance player, NtfSyncPlayer sync)
	{
		PlayerAttr attr = player.Attributes.Set(AttrGid, GrantedFlagSid, 1u);
		player.Attributes.SyncTo(sync, attr);
	}

	/// <summary>
	/// 每个品质各发 perQuality 个。返回实际发放明细；槽位不够时提前停并置 SlotsFull。
	/// </summary>
	public static GrantResult GrantEachQuality(PlayerInstance player, int perQuality, NtfSyncPlayer sync)
	{
		GrantResult result = new GrantResult();
		if (perQuality <= 0)
		{
			perQuality = 20;
		}
		Dictionary<uint, Rogue3DScoreModel_BuffLevelSettlement.PartCfg>? parts = Rogue3DScoreModel_BuffLevelSettlement.Parts();
		Dictionary<uint, List<Rogue3DScoreModel_BuffLevelSettlement.BuffCfg>>? buffs = Rogue3DScoreModel_BuffLevelSettlement.BuffsByGroup();
		if (parts == null || parts.Count == 0 || buffs == null || buffs.Count == 0)
		{
			return result;
		}

		List<int> qualities = parts.Values.Select((Rogue3DScoreModel_BuffLevelSettlement.PartCfg p) => p.Quality)
			.Distinct()
			.OrderBy((int q) => q)
			.ToList();
		List<(int Type, int SubType)> shapes = parts.Values.Select((Rogue3DScoreModel_BuffLevelSettlement.PartCfg p) => (p.Type, p.SubType))
			.Distinct()
			.OrderBy<(int Type, int SubType), int>(((int Type, int SubType) x) => x.Type)
			.ThenBy<(int Type, int SubType), int>(((int Type, int SubType) x) => x.SubType)
			.ToList();
		if (shapes.Count == 0)
		{
			return result;
		}

		int slot = Rogue3DScoreModel_BuffLevelSettlement.FindFreeSlot(player);
		foreach (int quality in qualities)
		{
			int got = 0;
			for (int i = 0; i < perQuality; i++)
			{
				if (slot < 0)
				{
					result.SlotsFull = true;
					break;
				}
				(int Type, int SubType) shape = shapes[i % shapes.Count];
				uint partId = (uint)((shape.Type - 1) * 9 + (3 - quality) * 3 + shape.SubType);
				if (!parts.TryGetValue(partId, out Rogue3DScoreModel_BuffLevelSettlement.PartCfg? part) || part == null)
				{
					continue;
				}
				if (!buffs.TryGetValue(part.BuffGroupId, out List<Rogue3DScoreModel_BuffLevelSettlement.BuffCfg>? pool) || pool == null || pool.Count == 0)
				{
					continue;
				}
				Rogue3DScoreModel_BuffLevelSettlement.BuffCfg buff = Rogue3DScoreModel_BuffLevelSettlement.PickBuff(pool);
				uint data = buff.RollValue();
				uint packed = (partId & 0x3Fu) | ((buff.Id & 0xFFFu) << 6) | ((data & 0x3FFu) << 18);
				PlayerAttr attr = player.Attributes.Set(AttrGid, (uint)slot, packed);
				player.Attributes.SyncTo(sync, attr);
				got++;
				slot = Rogue3DScoreModel_BuffLevelSettlement.FindFreeSlot(player);
			}
			result.ByQuality.Add((quality, got));
			result.Total += got;
			if (result.SlotsFull)
			{
				break;
			}
		}
		result.SlotsUsed = PartEnd - PartBegin - CountFree(player);
		return result;
	}

	public static int CountFree(PlayerInstance player)
	{
		int n = 0;
		for (int sid = PartBegin; sid < PartEnd; sid++)
		{
			if (player.Attributes.GetValue(AttrGid, (uint)sid) == 0)
			{
				n++;
			}
		}
		return n;
	}
}
