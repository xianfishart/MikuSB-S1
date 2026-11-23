using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.BagRogue;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_NodeWalk")]
public class BagRogue_NodeWalk : CallGSHandler<BagRogueNodeWalkParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueNodeWalkParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		if (!BagRogueService.TryWalk(player, request.NodeId, sync, out string error))
		{
			return Task.FromResult(CallGSResult.Error(error, sync));
		}
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
