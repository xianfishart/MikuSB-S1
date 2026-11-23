using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Support;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

internal static class SupporterCardAffixShared
{
	public const uint BaseGid = 150u;

	public const uint FixedResetSid = 1u;

	public static SupportCardExcel? GetExcel(GameSupportCardInfo card)
	{
		return GameData.SupportCardData.FirstOrDefault((SupportCardExcel x) => x.TemplateId == card.TemplateId);
	}

	public static async Task SendResetResponse(Connection connection, NtfSyncPlayer? sync = null)
	{
		await CallGSRouter.SendScript(connection, "SupporterCard_ResetAffix", "null", sync);
	}

	public static async Task SendSelectResponse(Connection connection, NtfSyncPlayer? sync = null)
	{
		await CallGSRouter.SendScript(connection, "SupporterCard_SelectAffix", "null", sync);
	}

	public static List<Item> ConsumeCostItems(Connection connection, IEnumerable<IReadOnlyList<uint>> costs)
	{
		PlayerInstance player = connection.Player;
		List<Item> list = new List<Item>();
		foreach (IReadOnlyList<uint> cost in costs)
		{
			if (cost.Count >= 5)
			{
				ulong templateId = GameResourceTemplateId.FromGdpl(cost);
				BaseGameItemInfo baseGameItemInfo = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
				if (baseGameItemInfo == null || baseGameItemInfo.ItemCount < cost[4])
				{
					throw new InvalidOperationException("support affix material not enough");
				}
				baseGameItemInfo.ItemCount -= cost[4];
				Item item = baseGameItemInfo.ToProto();
				if (baseGameItemInfo.ItemCount == 0)
				{
					player.InventoryManager.InventoryData.Items.Remove(baseGameItemInfo.UniqueId);
					item.Count = 0u;
				}
				list.Add(item);
			}
		}
		return list;
	}

	public static bool HasEnoughItems(Connection connection, IEnumerable<IReadOnlyList<uint>> costs)
	{
		Dictionary<uint, BaseGameItemInfo>.ValueCollection items = connection.Player.InventoryManager.InventoryData.Items.Values;
		return costs.All((IReadOnlyList<uint> cost) =>
		{
			if (cost.Count < 5)
			{
				return false;
			}
			ulong templateId = GameResourceTemplateId.FromGdpl(cost);
			BaseGameItemInfo baseGameItemInfo = items.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId);
			return baseGameItemInfo != null && baseGameItemInfo.ItemCount >= cost[4];
		});
	}

	public static PlayerAttr GetOrCreateAttr(PlayerGameData data, uint gid, uint sid)
	{
		PlayerAttr playerAttr = data.Attrs.FirstOrDefault((PlayerAttr x) => x.Gid == gid && x.Sid == sid);
		if (playerAttr != null)
		{
			return playerAttr;
		}
		playerAttr = new PlayerAttr
		{
			Gid = gid,
			Sid = sid,
			Val = 0u
		};
		data.Attrs.Add(playerAttr);
		return playerAttr;
	}

	public static void SetAttr(Connection connection, NtfSyncPlayer sync, uint gid, uint sid, uint value)
	{
		PlayerInstance player = connection.Player;
		PlayerAttr orCreateAttr = GetOrCreateAttr(player.Data, gid, sid);
		orCreateAttr.Val = value;
		sync.Custom[player.ToPackedAttrKey(gid, sid)] = value;
		sync.Custom[player.ToShiftedAttrKey(gid, sid)] = value;
	}

	public static IEnumerable<uint> GetActiveAffixIds(GameSupportCardInfo card, params int[] ignoreSlots)
	{
		HashSet<int> ignored = ignoreSlots.ToHashSet();
		for (int slot = 1; slot <= 3; slot++)
		{
			if (!ignored.Contains(slot))
			{
				var (affixId, _) = SupportAffixStateService.GetAffix(card, slot);
				if (affixId != 0)
				{
					yield return affixId;
				}
			}
		}
	}

	public static void Save(Connection connection)
	{
		PlayerInstance player = connection.Player;
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.Data);
	}
}
