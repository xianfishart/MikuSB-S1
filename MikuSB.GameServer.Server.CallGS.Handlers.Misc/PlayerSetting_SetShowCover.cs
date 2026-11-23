using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetShowCover")]
public class PlayerSetting_SetShowCover : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SetShowCoverParam req = JsonSerializer.Deserialize<SetShowCoverParam>(param);
		if (req != null)
		{
			BaseGameItemInfo item = player.InventoryManager.GetNormalItem(req.Id);
			if (item == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_SetShowCover", "{\"err\":\"error.BadParam\"}");
				return;
			}
			player.SetShowItem(17, item.UniqueId);
			DatabaseHelper.SaveDatabaseType(player.Data);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			sync.ShowItems.AddRange(player.Data.ShowItems);
			await CallGSRouter.SendScript(connection, "PlayerSetting_SetShowCover", "null", sync);
		}
	}
}
