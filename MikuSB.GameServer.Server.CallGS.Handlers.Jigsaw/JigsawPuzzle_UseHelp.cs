using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_UseHelp")]
public class JigsawPuzzle_UseHelp : CallGSHandler<JigsawUseHelpParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_UseHelp");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawUseHelpParam request)
	{
		PlayerInstance player = context.Connection.Player;
		JigsawScheduleExcel jigsawScheduleExcel = JigsawPuzzleService.FindSchedule(request.ActId);
		if (jigsawScheduleExcel == null || JigsawPuzzleService.FindPiece(request.ActId, request.PieceId) == null)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		long helpTime = JigsawPuzzleService.GetHelpTime(player, request.ActId);
		if (helpTime > 0 && num - helpTime < jigsawScheduleExcel.HitInterval)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JigsawPuzzleService.SetHelpTime(player, request.ActId, num, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"UseHelp: schedule={request.ActId} piece={request.PieceId} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nId"] = request.PieceId,
			["nActId"] = request.ActId
		}, sync));
	}
}
