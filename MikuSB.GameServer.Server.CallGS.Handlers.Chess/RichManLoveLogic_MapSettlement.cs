using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_MapSettlement")]
public class RichManLoveLogic_MapSettlement : CallGSHandler<RichmanSettlementParam>
{
	private static readonly Logger Log = new Logger("RichManLoveLogic_MapSettlement");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanSettlementParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		bool value = ChessActivityService.TryCloseSession(player, request.Seed, 3u, request.MapId);
		Log.Info($"uid={player.Uid} richman-love map={request.MapId} seed={request.Seed} settled={value}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject()));
	}
}
