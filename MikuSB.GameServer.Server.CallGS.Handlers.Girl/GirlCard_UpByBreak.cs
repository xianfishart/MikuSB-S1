using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_UpByBreak")]
public class GirlCard_UpByBreak : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		GirlCardUpByBreakParam req = JsonSerializer.Deserialize<GirlCardUpByBreakParam>(param);
		if (req == null || req.CardId == 0 || req.BreakLv == 0 || req.Materials == null || req.Materials.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpByBreak", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		CharacterInfo card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
		if (card == null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpByBreak", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		uint expectedBreakLv = card.Break + 1;
		if (req.BreakLv != expectedBreakLv)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpByBreak", "{\"sErr\":\"error.BadParam\"}");
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
					ulong templateId = GameResourceTemplateId.FromGdpl(genre, detail, particular, level);
					requestedMaterials[templateId] = requestedMaterials.GetValueOrDefault(templateId) + count;
				}
			}
		}
		if (requestedMaterials.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UpByBreak", "{\"sErr\":\"tip.not_material_for_break\"}");
			return;
		}
		List<Item> syncItems = new List<Item>();
		ulong key;
		uint value;
		foreach (KeyValuePair<ulong, uint> item3 in requestedMaterials)
		{
			item3.Deconstruct(out key, out value);
			ulong templateId2 = key;
			uint count2 = value;
			BaseGameItemInfo item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == templateId2);
			if (item == null || item.ItemCount < count2)
			{
				await CallGSRouter.SendScript(connection, "GirlCard_UpByBreak", "{\"sErr\":\"tip.not_material_for_break\"}");
				return;
			}
		}
		foreach (KeyValuePair<ulong, uint> item4 in requestedMaterials)
		{
			item4.Deconstruct(out key, out value);
			ulong templateId3 = key;
			uint count3 = value;
			BaseGameItemInfo item2 = player.InventoryManager.InventoryData.Items.Values.First((BaseGameItemInfo x) => x.TemplateId == templateId3);
			item2.ItemCount -= count3;
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				syncItems.Add(BuildRemovedProto(item2));
			}
			else
			{
				syncItems.Add(item2.ToProto());
			}
		}
		card.Break = req.BreakLv;
		syncItems.Add(card.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		await CallGSRouter.SendScript(connection, "GirlCard_UpByBreak", "{}", sync);
	}

	private static Item BuildRemovedProto(BaseGameItemInfo item)
	{
		Item item2 = item.ToProto();
		item2.Count = 0u;
		return item2;
	}
}
