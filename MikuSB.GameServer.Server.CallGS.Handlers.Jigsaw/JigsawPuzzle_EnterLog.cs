using System;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_EnterLog")]
public class JigsawPuzzle_EnterLog : CallGSHandler<JigsawEnterLogParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_EnterLog");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawEnterLogParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (JigsawPuzzleService.FindSchedule(request.ScheduleId) == null)
		{
			return Task.FromResult(CallGSResult.NoResponse());
		}
		JigsawPuzzleService.SetEnterTime(player, request.ScheduleId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), null);
		Log.Info($"EnterLog: schedule={request.ScheduleId} type={request.EnterType} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.NoResponse());
	}
}
