using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("Weapon_ShowDefaultPart")]
public class Weapon_ShowDefaultPart : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		WeaponShowDefaultPartParam req = JsonSerializer.Deserialize<WeaponShowDefaultPartParam>(param);
		if (req == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_ShowDefaultPart", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		PlayerInstance player = connection.Player;
		GameWeaponInfo weaponData = player.InventoryManager.GetWeaponItem(req.Id);
		if (weaponData == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_ShowDefaultPart", "{}");
			return;
		}
		if (req.Flag == 1)
		{
			weaponData.Flag = ItemFlagEnum.FLAG_WEAPON_DEFAULT;
		}
		else
		{
			weaponData.Flag = ItemFlagEnum.FLAG_READED;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = { weaponData.ToProto() }
		};
		await CallGSRouter.SendScript(connection, "Weapon_ShowDefaultPart", "null", sync);
	}
}
