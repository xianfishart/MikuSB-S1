using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_ChangeAccountShowSkin")]
public class PlayerSetting_ChangeAccountShowSkin : ICallGSHandler
{
	private static readonly int[] SkinSlots = new int[5] { 9, 10, 11, 12, 13 };

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		ChangeAccountShowParam req = JsonSerializer.Deserialize<ChangeAccountShowParam>(param);
		if (req == null || req.Index < 1 || req.Index > SkinSlots.Length)
		{
			return;
		}
		if (req.ItemId < 0)
		{
			player.SetShowItem(SkinSlots[req.Index - 1], 0uL);
		}
		else
		{
			GameSkinInfo skin = player.InventoryManager.GetSkinItem((uint)req.ItemId);
			if (skin == null)
			{
				await CallGSRouter.SendScript(connection, "PlayerSetting_ChangeAccountShowSkin", "{\"sErr\":\"error.BadParam\"}");
				return;
			}
			player.SetShowItem(SkinSlots[req.Index - 1], skin.UniqueId);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.ShowItems.AddRange(player.Data.ShowItems);
		await CallGSRouter.SendScript(connection, "PlayerSetting_ChangeAccountShowSkin", "null", sync);
	}
}
