using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GirlRegister")]
public class GirlRegister : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		int girlId = HouseJson.NumField(root, "GirlId");
		int floorId = HouseJson.NumField(root, "FloorId");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (girlId > 0)
		{
			uint bedroomSid = HouseAttr.GetNextBedroomSid(connection.Player, (uint)floorId);
			if (bedroomSid == 0)
			{
				await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"GirlRegister\",\"sErr\":\"house.NoRoom\"}");
				return;
			}
			ulong cardId = GameResourceTemplateId.FromGdpl(5u, 26u, 1u, 4u);
			BaseGameItemInfo card = connection.Player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == cardId);
			if (card == null || card.ItemCount < 1)
			{
				await CallGSRouter.SendScript(connection, "House_Request", "{\"FuncName\":\"GirlRegister\",\"sErr\":\"tip.house.LackItem\"}");
				return;
			}
			card.ItemCount--;
			if (card.ItemCount == 0)
			{
				connection.Player.InventoryManager.InventoryData.Items.Remove(card.UniqueId);
			}
			sync.Items.Add(card.ToProto());
			uint roomId = bedroomSid - 2550;
			await HouseAttr.SetAsync(connection, HouseAttr.GirlRoomNumSid(girlId), roomId, sync);
			await HouseAttr.SetAsync(connection, bedroomSid, (uint)girlId, sync);
			DatabaseHelper.SaveDatabaseType(connection.Player.InventoryManager.InventoryData);
		}
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Synthesize(root), sync);
	}
}
