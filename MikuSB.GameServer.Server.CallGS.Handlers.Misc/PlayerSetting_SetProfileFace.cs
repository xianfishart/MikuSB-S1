using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetProfileFace")]
public class PlayerSetting_SetProfileFace : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SetProfileFaceParam req = JsonSerializer.Deserialize<SetProfileFaceParam>(param);
		if (req == null)
		{
			return;
		}
		if (req.HeadItemId != 0)
		{
			BaseGameItemInfo item = player.InventoryManager.GetNormalItem(req.HeadItemId);
			if (item == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_SetProfileFace", "{\"err\":\"error.BadParam\"}");
				return;
			}
			player.SetShowItem(5, item.UniqueId);
		}
		if (req.FrameItemId != 0)
		{
			BaseGameItemInfo item2 = player.InventoryManager.GetNormalItem(req.FrameItemId);
			if (item2 == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_SetProfileFace", "{\"err\":\"error.BadParam\"}");
				return;
			}
			player.SetShowItem(6, item2.UniqueId);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.ShowItems.AddRange(player.Data.ShowItems);
		await CallGSRouter.SendScript(connection, "PlayerSetting_SetProfileFace", "null", sync);
	}
}
