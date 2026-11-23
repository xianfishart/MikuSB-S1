using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.Database.Account;
using MikuSB.Database.Player;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Inventory;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GM;

[CallGSApi("GM_QuickGrow")]
public class GM_QuickGrow : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (!AccountData.HasPerm(new PermEnum[1] { PermEnum.Admin }, player.Uid))
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		player.AddPlayerExperience(1000000u, sync);
		foreach (SuppliesExcel supplies in GameData.AllSuppliesData)
		{
			if (!ItemSanitizer.IsBanned(supplies.Genre, supplies.Detail, supplies.Particular, supplies.Level))
			{
				await player.InventoryManager.AddSuppliesItem(supplies, 90000u, sendPacket: false);
			}
		}
		HashSet<uint> levelIds = new HashSet<uint>();
		AddLevels(levelIds, GameData.ChapterLevelData.Keys);
		AddLevels(levelIds, GameData.RoleLevelData.Keys);
		AddLevels(levelIds, GameData.DailyLevelData.Keys);
		AddLevels(levelIds, GameData.TowerLevelData.Keys);
		AddLevels(levelIds, GameData.TowerEventLevelData.Keys);
		AddLevels(levelIds, GameData.VirCaptureLevelData.Keys);
		AddLevels(levelIds, GameData.VirCaptureTowerData.Keys);
		AddLevels(levelIds, GameData.ElementLevelData.Keys);
		AddLevels(levelIds, GameData.DlcChapterLevelData.Keys);
		AddLevels(levelIds, GameData.RepressionLevelData.Keys);
		AddLevels(levelIds, GameData.DlcClimbTowerLevelData.Keys);
		AddLevels(levelIds, GameData.BagRogueLevelData.Keys);
		AddLevels(levelIds, GameData.RogueLevelData.Keys);
		AddLevels(levelIds, GameData.DefendLevelData.Keys);
		AddLevels(levelIds, GameData.FunTimedLevelData.Keys);
		AddLevels(levelIds, GameData.GirlRushLevelData.Keys);
		AddLevels(levelIds, GameData.SeasonBossLevelData.Keys);
		AddLevels(levelIds, GameData.WorldBossLevelData.Keys);
		AddLevels(levelIds, GameData.DonoRogueLevelData.Keys);
		AddLevels(levelIds, GameData.FarmWarLevelData.Keys);
		AddLevels(levelIds, GameData.FishingLevelData.Keys);
		foreach (uint levelId in levelIds)
		{
			PlayerAttr attr = player.Attributes.GetOrCreate(22u, levelId);
			if (attr.Val == 0)
			{
				attr.Val = 1700000000u;
				player.Attributes.SyncTo(sync, attr);
			}
		}
		TaskProgressService.Ensure(player);
		DailyPointService.Recompute(player, sync);
		foreach (PlayerAttr attr2 in player.Data.Attrs.Where((PlayerAttr x) =>
		{
			uint gid = x.Gid;
			return (gid == 2 || gid == 22) ? true : false;
		}))
		{
			player.Attributes.SyncTo(sync, attr2);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		await connection.SendPacket(1004, sync, 0);
	}

	private static void AddLevels(HashSet<uint> target, IEnumerable<uint> ids)
	{
		foreach (uint id in ids)
		{
			if (id != 0)
			{
				target.Add(id);
			}
		}
	}
}
