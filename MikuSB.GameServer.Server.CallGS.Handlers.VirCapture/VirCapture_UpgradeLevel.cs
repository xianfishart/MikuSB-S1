using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 新星开拓「伙伴编队 → 技能升阶」（第 342 节新增）。
///
/// 客户端链路（运行时 dump 确认）：
///   umg_vircapture_formation:ClickSkill → UI.SafeOpen("VirCaptureSkillUp", nItemId)
///   uw_vircapture_skillitem:ClickUp
///     取 tbParam.nSkillId / nSkillLevel
///     VirCaptureConfig.GetSkillCostConfig(skill.nCostId).tbLevelCost[当前等级]
///     逐条 me:GetItemCount(g,d,p,l) 校验 → 不够就 tip.not_material
///     self.bSendSkillUp = true
///     VirCapture.DoUpgradeLevel(nItemId, nSkillId)
///       me:CallGS("VirCapture_UpgradeLevel", {nId = nItemId, nSkillId = nSkillId})
///   回调 s2c.tbCmds.VirCapture_UpgradeLevel:
///     UI.CloseConnection(); 有 sErr 就 ShowTip 返回
///     UI.GetUI("VirCaptureSkillUp"):OnReceiveUpdate(); ShowTip("tip.weapon_level_up")
///
/// 之前这个 api 没有实现，点升阶只会回 ui.TxtNotOpen，所以"用不了"。
/// 技能等级就存在卡的 Affixs 槽 4..6（[技能Id, 等级]），等级 1..6。
/// </summary>
[CallGSApi("VirCapture_UpgradeLevel")]
public class VirCapture_UpgradeLevel : ICallGSHandler
{
	private const int FirstSkillSlot = 4;

	private const int LastSkillSlot = 6;

	private const uint MaxSkillLevel = 6u;

	private const int CostId = 1;

	private static readonly Logger Log = new Logger("VirCapture.SkillUp");

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		UpgradeParam req = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(param))
			{
				req = JsonSerializer.Deserialize<UpgradeParam>(param);
			}
		}
		catch (JsonException)
		{
		}
		if (req == null || req.ItemId == 0u || req.SkillId == 0u)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		MonsterCardItemInfo card = GetMonsterCard(player, req.ItemId);
		if (card == null)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		card.EnsureAffixs();
		int slot = card.FindSkillSlot(req.SkillId);
		if (slot < FirstSkillSlot || slot > LastSkillSlot)
		{
			await Send(connection, "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint level = card.GetSkillLevel(slot);
		if (level == 0u)
		{
			level = 1u;
			card.SetSkillLevel(slot, level);
		}
		if (level >= MaxSkillLevel)
		{
			await Send(connection, "{\"sErr\":\"ui.TxtLevelupMax\"}");
			return;
		}

		// 与客户端完全一致的消耗表：tbLevelCost[当前等级]。
		List<List<uint>> costs = VirCaptureSkillCostConfig.Instance.GetLevelCost(CostId, level);
		if (costs == null || costs.Count == 0)
		{
			await Send(connection, "{\"sErr\":\"tip.not_material\"}");
			return;
		}
		// 先整体校验，避免扣一半失败。
		foreach (List<uint> cost in costs)
		{
			BaseGameItemInfo owned = FindCostItem(player, cost);
			if (owned == null || owned.ItemCount < cost[4])
			{
				await Send(connection, "{\"sErr\":\"tip.not_material\"}");
				return;
			}
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		foreach (List<uint> cost in costs)
		{
			BaseGameItemInfo owned = FindCostItem(player, cost);
			if (owned != null)
			{
				owned.ItemCount -= cost[4];
				sync.Items.Add(owned.ToProto());
			}
		}

		uint next = level + 1u;
		card.SetSkillLevel(slot, next);
		VirCaptureCardSkillStore.Capture(player.Uid, card);
		sync.Items.Add(card.ToProto());
		// 开拓目标：213「累计进行技能升阶」
		VirCaptureMissionProgress.Add(player, VirCaptureMissionProgress.KeySkillUp);
		VirCaptureMissionProgress.SyncAll(player, sync);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		VirCaptureCardSkillStore.Save(player.Uid);
		Log.Info($"UpgradeLevel uid={player.Uid} card={card.UniqueId} skill={req.SkillId} {level}->{next}");
		await CallGSRouter.SendScript(connection, "VirCapture_UpgradeLevel", "{}", sync);
	}

	private static BaseGameItemInfo? FindCostItem(PlayerInstance player, List<uint> cost)
	{
		if (cost == null || cost.Count < 5)
		{
			return null;
		}
		return player.InventoryManager.GetNormalItemGDPL((ItemTypeEnum)cost[0], cost[1], cost[2], cost[3]);
	}

	/// <summary>取卡；如果 DB 里还原成了基类，这里顺手换成带 Affixs 的子类。</summary>
	private static MonsterCardItemInfo? GetMonsterCard(PlayerInstance player, uint itemId)
	{
		if (!player.InventoryManager.InventoryData.Items.TryGetValue(itemId, out BaseGameItemInfo item))
		{
			return null;
		}
		if (item.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
		{
			return null;
		}
		if (item is MonsterCardItemInfo card)
		{
			return card;
		}
		MonsterCardItemInfo converted = new MonsterCardItemInfo
		{
			UniqueId = item.UniqueId,
			TemplateId = item.TemplateId,
			ItemCount = item.ItemCount,
			ItemType = item.ItemType,
			Flag = item.Flag,
			Level = item.Level,
			Exp = item.Exp
		};
		converted.EnsureAffixs();
		player.InventoryManager.InventoryData.Items[itemId] = converted;
		VirCaptureCardSkillStore.Apply(player.Uid, converted);
		return converted;
	}

	private static Task Send(Connection connection, string arg)
	{
		return CallGSRouter.SendScript(connection, "VirCapture_UpgradeLevel", arg);
	}

	private sealed class UpgradeParam
	{
		[JsonPropertyName("nId")]
		public uint ItemId { get; set; }

		[JsonPropertyName("nSkillId")]
		public uint SkillId { get; set; }
	}
}
