using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichmanLogic_MapSettlement")]
public class RichmanLogic_MapSettlement : CallGSHandler<RichmanSettlementParam>
{
	private static readonly Logger Log = new Logger("RichmanLogic_MapSettlement");

	protected override async Task<CallGSResult> HandleAsync(CallGSContext context, RichmanSettlementParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return await Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint activityId = ((request.ActivityId != 0) ? request.ActivityId : ChessActivityService.SelectedId(DateTime.Now));
		uint mapId = request.MapId;
		if (!ChessActivityService.TryCloseSession(player, request.Seed, activityId, mapId))
		{
			Log.Warn($"uid={player.Uid} richman settlement without a matching session (activity={activityId} map={mapId} seed={request.Seed}); replay or missing EnterMap.");
			return CallGSResult.Ok(new JsonObject { ["RewardNum"] = 0 });
		}
		uint reward = await ChessActivityService.PayRankRewardAsync(player, activityId, mapId, request.Rank, sync);
		Log.Info($"uid={player.Uid} richman activity={activityId} map={mapId} rank={request.Rank} reason={request.Log?[3]} reward={reward}.");
		return CallGSResult.Ok(new JsonObject { ["RewardNum"] = reward }, sync);
	}
}
