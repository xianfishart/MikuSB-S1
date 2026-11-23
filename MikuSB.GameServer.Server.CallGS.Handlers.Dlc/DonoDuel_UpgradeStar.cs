using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

[CallGSApi("DonoDuel_UpgradeStar")]
public class DonoDuel_UpgradeStar : CallGSHandler
{
	private static readonly Logger Log = new Logger("DonoDuel_UpgradeStar");

	private const int MaxEquivalenceDepth = 10;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		JsonObject root = DonoDuelGrowth.ParseObject(param);
		uint uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
		uint targetStar = DonoDuelGrowth.ReadUint(root?["nSourStar"]);
		List<uint> materialIds = DonoDuelGrowth.ReadUintList(root?["tbMaterial"]);
		if (!DonoDuelBag.HasCard(player, uniqueId))
		{
			Log.Warn($"Donoduel star upgrade for unknown card {uniqueId} from uid={player.Uid}.");
			return DonoDuelGrowth.Bad();
		}
		uint templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
		uint currentStar = DonoDuelBag.GetStar(player, uniqueId);
		if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out DonoDuelHeroCardExcel heroCard))
		{
			Log.Warn($"Donoduel star upgrade for card {uniqueId} with unknown hero {templateId}.");
			return DonoDuelGrowth.Bad();
		}
		uint maxStar = ((heroCard.Stars == 0) ? 5u : heroCard.Stars);
		if (targetStar <= currentStar || targetStar > maxStar)
		{
			Log.Info($"Donoduel star upgrade of card {uniqueId} rejected for uid={player.Uid}: target {targetStar} vs current {currentStar}, max {maxStar}.");
			return DonoDuelGrowth.Bad();
		}
		uint steps = targetStar - currentStar;
		if (steps > 10)
		{
			return DonoDuelGrowth.Bad();
		}
		uint required = Power2(targetStar - 1);
		uint provided = 0u;
		List<uint> materials = new List<uint>();
		uint inheritedQuality = DonoDuelBag.GetQuality(player, uniqueId);
		foreach (uint materialId in materialIds)
		{
			if (materialId != uniqueId && !materials.Contains(materialId))
			{
				if (!DonoDuelBag.HasCard(player, materialId))
				{
					Log.Info($"Donoduel star upgrade of card {uniqueId} rejected for uid={player.Uid}: material {materialId} does not exist.");
					return DonoDuelGrowth.Bad();
				}
				if (DonoDuelBag.GetTemplateId(player, materialId) != templateId)
				{
					Log.Info($"Donoduel star upgrade of card {uniqueId} rejected for uid={player.Uid}: material {materialId} is a different hero ({DonoDuelBag.GetTemplateId(player, materialId)} vs {templateId}).");
					return DonoDuelGrowth.Bad();
				}
				// 第 464 节：出阵中的卡也允许当材料（原来直接拒绝，客户端还会把它列出来，
				// 于是玩家只看到「请求参数错误」）。消耗前会先把它从编队里摘掉。
				// 第 486 节：材料星级可以低于目标卡 —— 官方是「2 的幂」等价，
				// 1 星卡值 1、2 星值 2、3 星值 4……所以拿一堆 1 星去填 3 星升 4 星是合法的。
				// 原来这里要求 star ≥ currentStar，再用 star - currentStar 算 3 的幂，
				// 低星材料会 uint 下溢成天文数字，被 exponent > 10 直接拒掉（一键提升必失败）。
				uint star = DonoDuelBag.GetStar(player, materialId);
				if (star == 0 || star > 10)
				{
					Log.Info($"Donoduel star upgrade of card {uniqueId} rejected for uid={player.Uid}: material {materialId} has invalid star {star}.");
					return DonoDuelGrowth.Bad();
				}
				provided += Power2(star - 1);
				inheritedQuality = Math.Max(inheritedQuality, DonoDuelBag.GetQuality(player, materialId));
				materials.Add(materialId);
			}
		}
		if (materials.Count == 0 || provided < required)
		{
			Log.Info($"Donoduel star upgrade of card {uniqueId} rejected for uid={player.Uid}: {provided} equivalents vs {required} required.");
			return DonoDuelGrowth.Bad();
		}
		uint quality = Math.Min(inheritedQuality, DonoDuelConfig.QualityMax());
		NtfSyncPlayer sync = new NtfSyncPlayer();
		DonoDuelBag.SetQuality(player, uniqueId, quality, sync);
		for (uint position = 1u; position <= 3; position++)
		{
			uint cap = DonoDuelGrowth.AttrCap(templateId, quality, position);
			if (cap != 0)
			{
				uint current = DonoDuelBag.GetAttr(player, uniqueId, position);
				if (current > cap)
				{
					DonoDuelBag.SetAttr(player, uniqueId, position, cap, sync);
				}
			}
		}
		DonoDuelBag.SetStar(player, uniqueId, targetStar, sync);
		foreach (uint materialId2 in materials)
		{
			// 出阵中的材料卡先下阵再销毁，否则编队里会留下一个不存在的卡位。
			DonoDuelBag.RemoveFromFormation(player, materialId2, sync);
			DonoDuelBag.DestroyCard(player, materialId2, sync);
		}
		// 第 483 节（追加）：升星要推进「养成之路」类剧情任务（301 五张 2 星…），
		// 否则任务一直停在 0/5，客户端不会提交、玩家也就领不到 Reward3 里的卡。
		int grown = DonoDuelTaskState.RecalcGrowthTasks(player, sync);
		if (grown > 0)
		{
			Log.Info($"Donoduel star upgrade advanced {grown} growth mission(s) for uid={player.Uid}.");
		}
		// 第 492 节：收集任务「多诺卡培养大师 1~4」（Type=3 / Objectives=4 / Param[-1,2]，
		// 累计提升星级次数）。只认 Param[1]=2 的那批，别把「累计提升潜能」也加进去。
		// 一次请求可能连升多级（targetStar - currentStar），计数按实际级数走。
		int counted = DonoDuelTaskState.AdvanceObjectiveMissions(player, DonoDuelTaskState.PotentialObjectiveId, sync, steps, DonoDuelTaskState.StarUpgradeKind);
		if (counted > 0)
		{
			Log.Info($"Donoduel star upgrade advanced {counted} collect mission(s) for uid={player.Uid}.");
		}
		DonoDuelBag.Save(player);
		await player.SendPacket(1004, sync);
		Log.Info($"Donoduel card {uniqueId} star {currentStar} -> {targetStar} (quality {quality}, {materials.Count} material(s), {provided}/{required} equivalents) for uid={player.Uid}.");
		return DonoDuelGrowth.Ok(new JsonObject
		{
			["nUniqueId"] = uniqueId,
			["nSourStar"] = targetStar
		});
	}

	private static uint Power2(uint exponent)
	{
		if (exponent > 20)
		{
			return uint.MaxValue;
		}
		uint num = 1u;
		for (int i = 0; i < exponent; i++)
		{
			num *= 2;
		}
		return num;
	}

	private static uint Power3(uint exponent)
	{
		uint num = 1u;
		for (int i = 0; i < exponent; i++)
		{
			num *= 3;
		}
		return num;
	}
}
