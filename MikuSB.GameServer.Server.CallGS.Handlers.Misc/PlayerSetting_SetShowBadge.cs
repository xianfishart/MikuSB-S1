using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("PlayerSetting_SetShowBadge")]
public class PlayerSetting_SetShowBadge : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		SetShowBadgeParam req = JsonSerializer.Deserialize<SetShowBadgeParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "PlayerSetting_SetShowBadge", "{\"err\":\"error.BadParam\"}");
			return;
		}
		ProfileShowItemTypeEnum[] slots = new ProfileShowItemTypeEnum[3]
		{
			ProfileShowItemTypeEnum.SHOWITEM_BADGE1,
			ProfileShowItemTypeEnum.SHOWITEM_BADGE2,
			ProfileShowItemTypeEnum.SHOWITEM_BADGE3
		};
		for (int i = 0; i < slots.Length; i++)
		{
			player.SetShowItem(itemId: (i < req.Badges.Count) ? req.Badges[i] : 0u, index: (int)slots[i]);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.ShowItems.AddRange(player.Data.ShowItems);
		await CallGSRouter.SendScript(connection, "PlayerSetting_SetShowBadge", "null", sync);
	}
}
