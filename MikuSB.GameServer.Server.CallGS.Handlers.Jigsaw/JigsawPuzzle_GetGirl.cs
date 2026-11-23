using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_GetGirl")]
public class JigsawPuzzle_GetGirl : CallGSHandler<JigsawGetGirlParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_GetGirl");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, JigsawGetGirlParam request)
	{
		PlayerInstance player = context.Connection.Player;
		JigsawScheduleExcel schedule = JigsawPuzzleService.FindSchedule(request.ScheduleId);
		if (schedule == null || schedule.Role.Count < 5)
		{
			Log.Warn($"GetGirl: unknown schedule {request.ScheduleId} for uid={player.Uid}.");
			return CallGSResult.Error("ui.TxtNotOpen");
		}
		if (JigsawPuzzleService.HasGirl(player, request.ScheduleId))
		{
			return CallGSResult.Error("achievement.RewardGeted");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JigsawPuzzleService.SetGirl(player, request.ScheduleId, sync);
		IReadOnlyList<uint> role = schedule.Role;
		List<IReadOnlyList<uint>> rows = new List<IReadOnlyList<uint>>
		{
			new List<uint>
			{
				role[0],
				role[1],
				role[2],
				role[3],
				role[4]
			}
		};
		await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		Log.Info($"GetGirl: schedule={request.ScheduleId} role=[{string.Join(',', role)}] for uid={player.Uid}.");
		JsonArray girl = new JsonArray();
		for (int index = 0; index < role.Count; index++)
		{
			girl.Add(role[index]);
		}
		return CallGSResult.Ok(new JsonObject { ["tbGirl"] = girl }, sync);
	}
}
