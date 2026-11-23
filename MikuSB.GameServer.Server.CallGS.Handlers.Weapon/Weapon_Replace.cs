using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Replace")]
public class Weapon_Replace : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		WeaponReplaceParam req = JsonSerializer.Deserialize<WeaponReplaceParam>(param);
		CharacterInfo cardData = ((req == null) ? null : player.CharacterManager.GetCharacterByGUID((uint)req.CardId));
		GameWeaponInfo newWeapon = ((req == null) ? null : player.InventoryManager.GetWeaponItem((uint)req.Id));
		if (cardData != null && newWeapon != null)
		{
			uint oldWeaponId = cardData.WeaponUniqueId;
			GameWeaponInfo oldWeapon = ((oldWeaponId == 0) ? null : player.InventoryManager.GetWeaponItem(oldWeaponId));
			cardData.WeaponUniqueId = newWeapon.UniqueId;
			newWeapon.EquipAvatarId = cardData.Guid;
			if (oldWeapon != null && oldWeapon.UniqueId != newWeapon.UniqueId)
			{
				oldWeapon.EquipAvatarId = 0u;
			}
			DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
			NtfSyncPlayer sync = new NtfSyncPlayer
			{
				Items = { cardData.ToProto() }
			};
			await CallGSRouter.SendScript(connection, "Weapon_Replace", "{}", sync);
		}
		else
		{
			await CallGSRouter.SendScript(connection, "Weapon_Replace", "{}");
		}
	}
}
