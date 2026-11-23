using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_SettlementUnlockEnd")]
public class RichManLoveLogic_SettlementUnlockEnd : CallGSHandler<RichmanUnlockEndParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanUnlockEndParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null || request.EndId == 0)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Set(131u, request.EndId, 1u);
		player.Attributes.SyncTo(sync, attr);
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nId"] = request.EndId }, sync));
	}
}
