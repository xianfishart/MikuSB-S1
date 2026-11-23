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

[CallGSApi("GirlSpine_UnlockNodeOneKey")]
public class GirlSpine_UnlockNodeOneKey : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		OneKeyUnlockParam req = JsonSerializer.Deserialize<OneKeyUnlockParam>(param);
		if (req == null || req.CardId == 0 || req.MastIdx <= 0 || req.SubIdxList == null || req.SubIdxList.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		ulong cardTemplateId = card.TemplateId;
		uint cardDetail = (uint)((cardTemplateId >> 16) & 0xFFFF);
		uint cardParticular = (uint)((cardTemplateId >> 32) & 0xFFFF);
		CardExcel cardExcel = GameData.CardData.Values.FirstOrDefault((CardExcel x) => x.Detail == cardDetail && x.Particular == cardParticular);
		Dictionary<ulong, uint> requestedMaterials = new Dictionary<ulong, uint>();
		if (cardExcel != null && GameData.SpineData.TryGetValue(cardExcel.SpineId, out SpineExcel spineExcel))
		{
			uint nodeCondId = spineExcel.GetNodeReq(req.MastIdx);
			if (nodeCondId != 0 && GameData.NodeConditionData.TryGetValue(nodeCondId, out NodeConditionExcel nodeCond))
			{
				int spineListIdx = req.MastIdx - 1;
				while (card.Spines.Count <= spineListIdx)
				{
					card.Spines.Add(0u);
				}
				uint currentMask = card.Spines[spineListIdx];
				foreach (int subIdx in req.SubIdxList)
				{
					if (subIdx <= 0)
					{
						continue;
					}
					uint bit = (uint)(1 << subIdx - 1);
					if ((currentMask & bit) != 0)
					{
						continue;
					}
					foreach (List<int> row in nodeCond.GetNodeCost(subIdx))
					{
						if (row.Count >= 5)
						{
							ulong tid = GameResourceTemplateId.FromGdpl((uint)row[0], (uint)row[1], (uint)row[2], (uint)row[3]);
							requestedMaterials[tid] = requestedMaterials.GetValueOrDefault(tid) + (uint)row[4];
						}
					}
				}
			}
		}
		ulong key;
		uint value;
		foreach (KeyValuePair<ulong, uint> item3 in requestedMaterials)
		{
			item3.Deconstruct(out key, out value);
			ulong tid2 = key;
			uint count = value;
			BaseGameItemInfo item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == tid2);
			if (item == null || item.ItemCount < count)
			{
				await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", "{\"sErr\":\"tip.not_material\"}");
				return;
			}
		}
		List<Item> syncItems = new List<Item>();
		foreach (KeyValuePair<ulong, uint> item4 in requestedMaterials)
		{
			item4.Deconstruct(out key, out value);
			ulong tid3 = key;
			uint count2 = value;
			BaseGameItemInfo item2 = player.InventoryManager.InventoryData.Items.Values.First((BaseGameItemInfo x) => x.TemplateId == tid3);
			item2.ItemCount -= count2;
			Item proto = item2.ToProto();
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				proto.Count = 0u;
			}
			syncItems.Add(proto);
		}
		int mastSpineIdx = req.MastIdx - 1;
		while (card.Spines.Count <= mastSpineIdx)
		{
			card.Spines.Add(0u);
		}
		foreach (int subIdx2 in req.SubIdxList)
		{
			if (subIdx2 > 0)
			{
				card.Spines[mastSpineIdx] |= (uint)(1 << subIdx2 - 1);
			}
		}
		syncItems.Add(card.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		int num;
		if (req.SubIdxList.Count <= 0)
		{
			num = 9;
		}
		else
		{
			List<int> subIdxList = req.SubIdxList;
			num = subIdxList[subIdxList.Count - 1];
		}
		int lastSubIdx = num;
		string rsp = $"{{\"tb\":{{\"D\":{cardDetail},\"pId\":{req.CardId},\"MastIdx\":{req.MastIdx},\"SubIdx\":{lastSubIdx}}}}}";
		await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", rsp, sync);
	}
}
