using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Character;
using MikuSB.Database.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Weapon;

[CallGSApi("Weapon_Exchange")]
public class Weapon_Exchange : CallGSHandler<WeaponExchangeParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, WeaponExchangeParam req)
	{
		PlayerInstance player = context.Connection.Player;
		if (req == null || req.CardIdA == 0 || req.CardIdB == 0 || req.CardIdA == req.CardIdB)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		CharacterInfo characterByGUID = player.CharacterManager.GetCharacterByGUID((uint)req.CardIdA);
		CharacterInfo characterByGUID2 = player.CharacterManager.GetCharacterByGUID((uint)req.CardIdB);
		if (characterByGUID == null || characterByGUID2 == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		GameWeaponInfo gameWeaponInfo = ((characterByGUID.WeaponUniqueId == 0) ? null : player.InventoryManager.GetWeaponItem(characterByGUID.WeaponUniqueId));
		GameWeaponInfo gameWeaponInfo2 = ((characterByGUID2.WeaponUniqueId == 0) ? null : player.InventoryManager.GetWeaponItem(characterByGUID2.WeaponUniqueId));
		characterByGUID.WeaponUniqueId = gameWeaponInfo2?.UniqueId ?? 0;
		characterByGUID2.WeaponUniqueId = gameWeaponInfo?.UniqueId ?? 0;
		if (gameWeaponInfo2 != null)
		{
			gameWeaponInfo2.EquipAvatarId = characterByGUID.Guid;
		}
		if (gameWeaponInfo != null)
		{
			gameWeaponInfo.EquipAvatarId = characterByGUID2.Guid;
		}
		DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		NtfSyncPlayer sync = new NtfSyncPlayer
		{
			Items = 
			{
				characterByGUID.ToProto(),
				characterByGUID2.ToProto()
			}
		};
		Logger.GetByClassName().Info($"Weapon exchange between cards {characterByGUID.Guid} and {characterByGUID2.Guid} for uid={player.Uid} (weapons {gameWeaponInfo?.UniqueId ?? 0} <-> {gameWeaponInfo2?.UniqueId ?? 0}).");
		return Task.FromResult(CallGSResult.Ok("{}", sync));
	}
}
