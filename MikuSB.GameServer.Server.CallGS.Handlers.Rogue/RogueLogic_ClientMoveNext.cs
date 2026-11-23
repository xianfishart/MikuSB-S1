using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_ClientMoveNext")]
public class RogueLogic_ClientMoveNext : CallGSHandler<RogueNodeParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueNodeParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		if (RogueService.FindNode(RogueService.CurrentMap(player), request.Node) == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		RogueService.MoveNext(player, request.Node, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
