using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Jigsaw;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Jigsaw;

[CallGSApi("JigsawPuzzle_GetPiece")]
public class JigsawPuzzle_GetPiece : CallGSHandler<JigsawGetPieceParam>
{
	private static readonly Logger Log = new Logger("JigsawPuzzle_GetPiece");

	private const uint MaxDraw = 10u;

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, JigsawGetPieceParam request)
	{
		PlayerInstance player = context.Connection.Player;
		JigsawScheduleExcel jigsawScheduleExcel = JigsawPuzzleService.FindSchedule(request.ScheduleId);
		if (jigsawScheduleExcel == null || jigsawScheduleExcel.PhasePieces.Count == 0)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		uint stage = request.Stage;
		if (stage == 0 || stage > jigsawScheduleExcel.PhasePieces.Count)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		long num2 = JigsawPuzzleService.StageTime(player, request.ScheduleId, request.ActivityId, (int)stage);
		if (num2 > num)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		uint num3 = Math.Clamp(request.Count, 1u, 10u);
		IReadOnlyList<uint> source = jigsawScheduleExcel.PhasePieces[(int)(stage - 1)];
		List<uint> list = (from pieceId in source
			where JigsawPuzzleService.FindPiece(request.ScheduleId, pieceId) != null
			where JigsawPuzzleService.GetPieceState(player, request.ScheduleId, pieceId) == 0
			select pieceId).ToList();
		if (list.Count == 0)
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
		}
		if (list.Count < num3)
		{
			num3 = (uint)list.Count;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!PortCurrency.TrySpend(player, jigsawScheduleExcel.KeyId, num3, sync, out string error))
		{
			return Task.FromResult(CallGSResult.Error(error));
		}
		JsonArray jsonArray = new JsonArray();
		foreach (uint item in list.Take((int)num3))
		{
			JigsawPuzzleService.SetPieceState(player, request.ScheduleId, item, 1u, sync);
			jsonArray.Add(item);
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
		Log.Info($"GetPiece: schedule={request.ScheduleId} stage={stage} drew={jsonArray.Count} for uid={player.Uid}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["tbGet"] = jsonArray }, sync));
	}
}
