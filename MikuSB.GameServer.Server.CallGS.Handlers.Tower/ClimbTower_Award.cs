using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.ClimbTower;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Tower;

[CallGSApi("ClimbTower_Award")]
public class ClimbTower_Award : CallGSHandler<ClimbTowerAwardParam>
{
	private static readonly Logger Log = new Logger("ClimbTower_Award");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ClimbTowerAwardParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return CallGSResult.Error("error.BadParam");
		}
		DlcClimbTowerSeasonExcel season = ClimbTowerService.SelectSeason(DateTime.Now);
		if (season == null)
		{
			Log.Warn("ClimbTower_Award: no season row loaded from dlc/climb_tower/climbtower_season.json.");
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray claimed = new JsonArray();
		JsonArray awards = new JsonArray();
		uint stars = ClimbTowerService.TotalStars(player, season);
		foreach (uint id in request.Ids)
		{
			if (!GameData.ClimbTowerRewardData.TryGetValue(id, out DlcClimbTowerRewardExcel cfg))
			{
				Log.Warn($"ClimbTower_Award: milestone {id} is not in climbtower_reward.json.");
				return CallGSResult.Error("error.BadParam");
			}
			if (stars < cfg.TargetNum)
			{
				return CallGSResult.Error("ui.TxtNotOpen", sync);
			}
			if (ClimbTowerService.IsAwarded(player, id))
			{
				return CallGSResult.Error("achievement.RewardGeted", sync);
			}
			List<List<uint>> rows = cfg.Rows;
			if (rows.Count > 0)
			{
				foreach (JsonNode item in await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync))
				{
					awards.Add(item?.DeepClone());
				}
			}
			ClimbTowerService.MarkAwarded(player, id, sync);
			claimed.Add(id);
			cfg = null;
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		if (claimed.Count > 0)
		{
			DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		}
		Log.Info($"ClimbTower_Award: uid={player.Uid} claimed [{string.Join(",", claimed)}] at {stars} stars ({awards.Count} reward rows).");
		return CallGSResult.Ok(new JsonObject
		{
			["tbAwardList"] = awards,
			["tbList"] = claimed
		}, sync);
	}
}
