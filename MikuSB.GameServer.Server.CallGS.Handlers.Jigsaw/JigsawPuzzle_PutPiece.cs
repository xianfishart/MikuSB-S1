using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_PutPiece")]
public class JigsawPuzzle_PutPiece : CallGSHandler<JigsawPutPieceParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_PutPiece");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawPutPieceParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (JigsawPuzzleService.FindPiece(request.ActId, request.PieceId) == null)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		uint pieceState = JigsawPuzzleService.GetPieceState(player, request.ActId, request.PieceId);
		if (pieceState != 1)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JigsawPuzzleService.SetPieceState(player, request.ActId, request.PieceId, 2u, sync);
		JigsawPuzzleService.ReportProgress(player, request.ActId, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		Log.Info($"PutPiece: schedule={request.ActId} piece={request.PieceId} placed={JigsawPuzzleService.CountPlaced(player, request.ActId)} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = request.PieceId }, sync));
	}
}
