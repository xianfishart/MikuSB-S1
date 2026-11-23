using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Break")]
public class Weapon_Break : ICallGSHandler
{
	private const uint MaxBreak = 6u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		WeaponBreakParam req = JsonSerializer.Deserialize<WeaponBreakParam>(param);
		if (req == null || req.WeaponId == 0)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Break", "\"error.BadParam\"");
			return;
		}
		GameWeaponInfo weapon = player.InventoryManager.InventoryData.Weapons.GetValueOrDefault((uint)req.WeaponId);
		if (weapon == null)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Break", "\"error.BadParam\"");
			return;
		}
		if (weapon.Break >= 6)
		{
			await CallGSRouter.SendScript(connection, "Weapon_Break", "\"tip.already_max_break\"");
			return;
		}
		uint nextBreak = weapon.Break + 1;
		WeaponExcel weaponExcel = GameData.WeaponData.Values.FirstOrDefault((WeaponExcel x) => GameResourceTemplateId.FromGdpl(x.Genre, x.Detail, x.Particular, x.Level) == weapon.TemplateId);
		Dictionary<ulong, uint> requestedMaterials = new Dictionary<ulong, uint>();
		if (weaponExcel != null && GameData.BreakData.TryGetValue(weaponExcel.BreakMatID, out BreakExcel breakExcel))
		{
			foreach (List<int> row in breakExcel.GetItems(nextBreak))
			{
				if (row.Count >= 5)
				{
					ulong tid = GameResourceTemplateId.FromGdpl((uint)row[0], (uint)row[1], (uint)row[2], (uint)row[3]);
					requestedMaterials[tid] = requestedMaterials.GetValueOrDefault(tid) + (uint)row[4];
				}
			}
		}
		ulong key;
		uint value;
		foreach (KeyValuePair<ulong, uint> item3 in requestedMaterials)
		{
			item3.Deconstruct(out key, out value);
			ulong tid2 = key;
			uint count = value;
			BaseGameItemInfo item = player.InventoryManager.InventoryData.Items.Values.FirstOrDefault((BaseGameItemInfo x) => x.TemplateId == tid2);
			if (item == null || item.ItemCount < count)
			{
				await CallGSRouter.SendScript(connection, "Weapon_Break", "\"tip.not_material_for_break\"");
				return;
			}
		}
		List<Item> syncItems = new List<Item>();
		foreach (KeyValuePair<ulong, uint> item4 in requestedMaterials)
		{
			item4.Deconstruct(out key, out value);
			ulong tid3 = key;
			uint count2 = value;
			BaseGameItemInfo item2 = player.InventoryManager.InventoryData.Items.Values.First((BaseGameItemInfo x) => x.TemplateId == tid3);
			item2.ItemCount -= count2;
			Item proto = item2.ToProto();
			if (item2.ItemCount == 0)
			{
				player.InventoryManager.InventoryData.Items.Remove(item2.UniqueId);
				proto.Count = 0u;
			}
			syncItems.Add(proto);
		}
		weapon.Break = nextBreak;
		syncItems.Add(weapon.ToProto());
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		TaskEventService.OnWeaponBreak(player);
		NtfSyncPlayer sync = new NtfSyncPlayer();
		sync.Items.AddRange(syncItems);
		await CallGSRouter.SendScript(connection, "Weapon_Break", "null", sync);
	}
}
