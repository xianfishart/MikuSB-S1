using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("Weapon_ReplacePart")]
public class Weapon_ReplacePart : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		WeaponPartReplaceParam req = JsonSerializer.Deserialize<WeaponPartReplaceParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_ReplacePart", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		GameWeaponInfo weaponData = player.InventoryManager.GetWeaponItem(req.Id);
		if (weaponData == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_ReplacePart", "{}");
			return;
		}
		uint partId = 0u;
		if (req.PartId != -1)
		{
			BaseGameItemInfo partData = player.InventoryManager.GetNormalItem((uint)req.PartId);
			if (partData != null)
			{
				partId = partData.UniqueId;
			}
		}
		weaponData.PartSlots[req.Type] = partId;
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { weaponData.ToProto() }
		};
		await CallGSRouter.SendScript(connection, "Weapon_ReplacePart", "null", sync);
	}
}
