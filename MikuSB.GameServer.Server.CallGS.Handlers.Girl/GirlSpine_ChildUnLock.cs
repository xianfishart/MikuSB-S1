using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlSpine_ChildUnLock")]
public class GirlSpine_ChildUnLock : ICallGSHandler
{
	private const uint SpineStrAttrGid = 30u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ChildUnLockParam req = JsonSerializer.Deserialize<ChildUnLockParam>(param);
		if (req == null || req.CardId == 0 || req.Info == null || req.Materials == null)
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
		int mastIdx = req.Info.Indx;
		int subIdx = req.Info.InSubIdx;
		if (mastIdx <= 0 || subIdx <= 0)
		{
			await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		int spineListIdx = mastIdx - 1;
		uint spineBit = (uint)(1 << subIdx - 1);
		while (card.Spines.Count <= spineListIdx)
		{
			card.Spines.Add(0u);
		}
		if ((card.Spines[spineListIdx] & spineBit) != 0)
		{
			await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", "{\"sErr\":\"tip.girlcard_alread_break\"}");
			return;
		}
		Dictionary<ulong, uint> requestedMaterials = new Dictionary<ulong, uint>();
		foreach (List<int> row in req.Materials)
		{
			if (row != null && row.Count >= 5)
			{
				uint genre = (uint)Math.Max(0, row[0]);
				uint detail = (uint)Math.Max(0, row[1]);
				uint particular = (uint)Math.Max(0, row[2]);
				uint level = (uint)Math.Max(0, row[3]);
				uint count = (uint)Math.Max(0, row[4]);
				if (genre != 0 && detail != 0 && particular != 0 && level != 0 && count != 0)
				{
					ulong tid = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
					requestedMaterials[tid] = requestedMaterials.GetValueOrDefault(tid) + count;
				}
			}
		}
		List<Item> syncItems = new List<Item>();
		ulong key;
		uint value;
		foreach (KeyValuePair<ulong, uint> item3 in requestedMaterials)
		{
			item3.Deconstruct(out key, out value);
			ulong tid2 = key;
			uint count2 = value;
			BaseGameItemInfo item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == tid2);
			if (item == null || item.ItemCount < count2)
			{
				await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", "{\"sErr\":\"tip.not_material\"}");
				return;
			}
		}
		foreach (KeyValuePair<ulong, uint> item4 in requestedMaterials)
		{
			item4.Deconstruct(out key, out value);
			ulong tid3 = key;
			uint count3 = value;
			BaseGameItemInfo item2 = player.InventoryManager.InventoryData.Items.Values.First((BaseGameItemInfo x) => x.TemplateId == tid3);
			item2.ItemCount -= count3;
			Item proto = item2.ToProto();
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				proto.Count = 0u;
			}
			syncItems.Add(proto);
		}
		card.Spines[spineListIdx] |= spineBit;
		syncItems.Add(card.ToProto());
		uint cardDetail = (uint)((card.TemplateId >> 16) & 0xFFFF);
		UpdateSpineStrAttr(particular: (uint)((card.TemplateId >> 32) & 0xFFFF), data: player.Data, detail: cardDetail, mastIdx: mastIdx);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		DatabaseHelper.SaveDatabaseType(player.Data);
		TaskEventService.OnSpineNode(player);
		string strAttrData = GetSpineStrAttrJson(player.Data, cardDetail);
		NtfSetStrAttr ntfStrAttr = new NtfSetStrAttr
		{
			Gid = 30u,
			Sid = cardDetail,
			Val = strAttrData
		};
		await connection.Player.SendPacket(1012, ntfStrAttr);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		string rsp = $"{{\"tb\":{{\"D\":{cardDetail},\"pId\":{req.CardId},\"MastIdx\":{mastIdx},\"SubIdx\":{subIdx}}}}}";
		await CallGSRouter.SendScript(connection, "GirlSpine_ChildUnLock", rsp, sync);
	}

	private static void UpdateSpineStrAttr(PlayerGameData data, uint detail, uint particular, int mastIdx)
	{
		PlayerStrAttr playerStrAttr = data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 30 && x.Sid == detail);
		if (playerStrAttr == null)
		{
			playerStrAttr = new PlayerStrAttr
			{
				Gid = 30u,
				Sid = detail,
				Val = "{}"
			};
			data.StrAttrs.Add(playerStrAttr);
		}
		Dictionary<string, SpineStrData> dictionary = JsonSerializer.Deserialize<Dictionary<string, SpineStrData>>(playerStrAttr.Val) ?? new Dictionary<string, SpineStrData>();
		string key = particular.ToString();
		if (!dictionary.TryGetValue(key, out var value))
		{
			value = new SpineStrData();
		}
		value.Ns = mastIdx;
		dictionary[key] = value;
		playerStrAttr.Val = JsonSerializer.Serialize(dictionary);
	}

	private static string GetSpineStrAttrJson(PlayerGameData data, uint detail)
	{
		return data.StrAttrs.FirstOrDefault((PlayerStrAttr x) => x.Gid == 30 && x.Sid == detail)?.Val ?? "{}";
	}
}
