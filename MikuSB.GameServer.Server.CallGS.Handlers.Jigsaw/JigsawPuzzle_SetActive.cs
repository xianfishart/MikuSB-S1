using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_SetActive")]
public class JigsawPuzzle_SetActive : CallGSHandler<JigsawSetActiveParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_SetActive");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawSetActiveParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (JigsawPuzzleService.FindSchedule(request.ScheduleId) == null)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		if (JigsawPuzzleService.IsActive(player, request.ScheduleId))
		{
			return Task.FromResult(CallGSResult.Error("achievement.RewardGeted"));
		}
		if (!JigsawPuzzleService.AllPlaced(player, request.ScheduleId))
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JigsawPuzzleService.SetActive(player, request.ScheduleId, sync);
		JigsawPuzzleService.ReportProgress(player, request.ScheduleId, sync);
		// 第 438 节：活动任务「收集情报 = 完成一次信源研析」（Type 334）。
		MikuSB.GameServer.Game.Activity.ActivityQuestService.Add(player, 334, 1, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"SetActive: schedule={request.ScheduleId} tuned for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = request.ScheduleId }, sync));
	}
}
