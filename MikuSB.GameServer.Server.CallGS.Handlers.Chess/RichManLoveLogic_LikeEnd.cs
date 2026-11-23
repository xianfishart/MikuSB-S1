using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_LikeEnd")]
public class RichManLoveLogic_LikeEnd : CallGSHandler<RichmanLikeEndParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanLikeEndParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null || request.EndId == 0)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		if (!ChessActivityService.IsEndUnlocked(player, request.EndId))
		{
			return Task.FromResult(CallGSResult.Error("ui.TxtArmLock4"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		ChessActivityService.MarkEndLiked(player, request.EndId, sync);
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = request.EndId }, sync));
	}
}
