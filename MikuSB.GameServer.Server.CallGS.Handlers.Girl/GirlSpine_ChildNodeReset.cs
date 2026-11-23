using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSpine_ChildNodeReset")]
public class GirlSpine_ChildNodeReset : CallGSHandler<ChildNodeResetParam>
{
	private const int MaxMastNum = 6;

	private const int MaxSubNum = 9;

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ChildNodeResetParam req)
	{
		PlayerInstance player = context.Connection.Player;
		if (req == null || req.CardId == 0)
		{
			return CallGSResult.Error("error.BadParam");
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
		if (card == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		uint cardDetail = (uint)((card.TemplateId >> 16) & 0xFFFF);
		uint cardParticular = (uint)((card.TemplateId >> 32) & 0xFFFF);
		Dictionary<ulong, uint> refundTotals = new Dictionary<ulong, uint>();
		CardExcel cardExcel = GameData.CardData.Values.FirstOrDefault((CardExcel x) => x.Detail == cardDetail && x.Particular == cardParticular);
		if (cardExcel != null && GameData.SpineData.TryGetValue(cardExcel.SpineId, out SpineExcel spineExcel))
		{
			for (int mastIdx = 1; mastIdx <= 6; mastIdx++)
			{
				int listIdx = mastIdx - 1;
				if (listIdx >= card.Spines.Count)
				{
					break;
				}
				uint mask = card.Spines[listIdx];
				if (mask == 0)
				{
					continue;
				}
				uint nodeCondId = spineExcel.GetNodeReq(mastIdx);
				if (nodeCondId == 0 || !GameData.NodeConditionData.TryGetValue(nodeCondId, out NodeConditionExcel nodeCond))
				{
					continue;
				}
				for (int subIdx = 1; subIdx <= 9; subIdx++)
				{
					if ((mask & (uint)(1 << subIdx - 1)) == 0)
					{
						continue;
					}
					foreach (List<int> row in nodeCond.GetNodeRefund(subIdx))
					{
						if (row != null && row.Count >= 5)
						{
							uint count = (uint)Math.Max(0, row[4]);
							if (count != 0)
							{
								ulong templateId = GameResourceTemplateId.FromGdpl((uint)Math.Max(0, row[0]), (uint)Math.Max(0, row[1]), (uint)Math.Max(0, row[2]), (uint)Math.Max(0, row[3]));
								refundTotals[templateId] = refundTotals.GetValueOrDefault(templateId) + count;
							}
						}
					}
				}
				nodeCond = null;
			}
		}
		List<Item> syncItems = new List<Item>();
		for (int i = 0; i < card.Spines.Count; i++)
		{
			card.Spines[i] = 0u;
		}
		syncItems.Add(card.ToProto());
		List<List<int>> awardedItems = new List<List<int>>();
		foreach (var (templateId2, count2) in refundTotals)
		{
			if (GameData.SuppliesByFullId.TryGetValue(templateId2, out SuppliesExcel supplies))
			{
				BaseGameItemInfo item = await player.InventoryManager.AddSuppliesItem(supplies, count2, sendPacket: false);
				if (item != null)
				{
					syncItems.Add(item.ToProto());
				}
				awardedItems.Add(new List<int>(5)
				{
					(int)supplies.Genre,
					(int)supplies.Detail,
					(int)supplies.Particular,
					(int)supplies.Level,
					(int)count2
				});
			}
			else
			{
				awardedItems.Add(new List<int>(5)
				{
					(int)(templateId2 & 0xFFFF),
					(int)((templateId2 >> 16) & 0xFFFF),
					(int)((templateId2 >> 32) & 0xFFFF),
					(int)((templateId2 >> 48) & 0xFFFF),
					(int)count2
				});
				supplies = null;
			}
		}
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		string rsp = JsonSerializer.Serialize(new
		{
			D = cardDetail,
			tbItem = awardedItems
		});
		return CallGSResult.Ok(rsp, sync);
	}
}
