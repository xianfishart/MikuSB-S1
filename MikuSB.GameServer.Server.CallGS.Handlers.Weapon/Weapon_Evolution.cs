using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Evolution")]
public class Weapon_Evolution : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		WeaponEvolutionParam req = JsonSerializer.Deserialize<WeaponEvolutionParam>(param);
		if (req == null || req.WeaponId == 0 || req.MaterialId == 0)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Evolution", "\"error.BadParam\"");
			return;
		}
		GameWeaponInfo weapon = player.InventoryManager.InventoryData.Weapons.GetValueOrDefault((uint)req.WeaponId);
		if (weapon == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Evolution", "\"error.BadParam\"");
			return;
		}
		List<Item> syncItems = new List<Item>();
		if (player.InventoryManager.InventoryData.Weapons.TryGetValue((uint)req.MaterialId, out GameWeaponInfo matWeapon))
		{
			player.InventoryManager.InventoryData.Weapons.Remove((uint)req.MaterialId);
			Item removed = matWeapon.ToProto();
			removed.Count = 0u;
			syncItems.Add(removed);
		}
		else
		{
			if (!player.InventoryManager.InventoryData.Items.TryGetValue((uint)req.MaterialId, out BaseGameItemInfo matItem))
			{
				await CallGSRouter.SendScript(connection, "Weapon_Evolution", "\"tip.not_material\"");
				return;
			}
			matItem.ItemCount--;
			Item proto = matItem.ToProto();
			if (matItem.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(matItem.UniqueId);
				proto.Count = 0u;
			}
			syncItems.Add(proto);
		}
		weapon.Evolue++;
		syncItems.Add(weapon.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		await CallGSRouter.SendScript(connection, "Weapon_Evolution", "null", sync);
	}
}
