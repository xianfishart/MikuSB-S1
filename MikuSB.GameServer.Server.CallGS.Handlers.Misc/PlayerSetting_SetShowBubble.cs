using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetShowBubble")]
public class PlayerSetting_SetShowBubble : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SetShowBubbleParam req = JsonSerializer.Deserialize<SetShowBubbleParam>(param);
		if (req != null)
		{
			BaseGameItemInfo item = player.InventoryManager.GetNormalItem(req.Id);
			if (item == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_SetShowBubble", "{\"err\":\"error.BadParam\"}");
				return;
			}
			player.SetShowItem(19, item.UniqueId);
			DatabaseHelper.SaveDatabaseType(player.Data);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.ShowItems.AddRange(player.Data.ShowItems);
			await CallGSRouter.SendScript(connection, "PlayerSetting_SetShowBubble", "null", sync);
		}
	}
}
