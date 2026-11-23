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
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_UnLockCharacter")]
public class GirlCard_UnLockCharacter : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		GirlCardUnlockParam req = JsonSerializer.Deserialize<GirlCardUnlockParam>(param);
		PlayerInstance player = connection.Player;
		if (req == null || req.Genre == 0 || req.Detail == 0 || req.Particular == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		if (player.CharacterManager.GetCharacterGDPL(ItemTypeEnum.TYPE_CARD, (int)req.Detail, (int)req.Particular) != null)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{\"sErr\":\"tip.girlcard_alread_break\"}");
			return;
		}
		CardExcel card = GameData.CardData.Values.FirstOrDefault((CardExcel x) => x.Genre == req.Genre && x.Detail == req.Detail && x.Particular == req.Particular && x.Level == req.Level);
		if (card == null || card.Pieces.Count == 0)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		List<int> piece = card.Pieces[0];
		if (piece.Count < 5)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		InventoryManager inv = player.InventoryManager;
		BaseGameItemInfo fragment = inv.GetNormalItemGDPL((ItemTypeEnum)piece[0], (uint)piece[1], (uint)piece[2], (uint)piece[3]);
		uint need = (uint)Math.Max(1, piece[4]);
		if (fragment == null || fragment.ItemCount < need)
		{
			await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{\"sErr\":\"tip.material_not_enough\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		fragment.ItemCount -= need;
		sync.Items.Add(fragment.ToProto());
		CharacterInfo character = await player.CharacterManager.AddCharacter(ItemTypeEnum.TYPE_CARD, req.Detail, req.Particular, req.Level, 1u, sendPacket: false);
		if (character == null)
		{
			fragment.ItemCount += need;
			await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		character.Level = 1u;
		sync.Items.Add(character.ToProto());
		if (fragment.ItemCount == 0)
		{
			inv.InventoryData.Items.Remove(fragment.UniqueId);
		}
		DatabaseHelper.SaveDatabaseType(inv.InventoryData);
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		await CallGSRouter.SendScript(connection, "GirlCard_UnLockCharacter", "{}", sync);
	}
}
