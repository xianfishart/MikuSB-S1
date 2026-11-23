using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_ChangeFormation")]
public class RogueLogic_ChangeFormation : CallGSHandler<RogueFormationParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RogueFormationParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		RogueService.Write(player, 5u, RogueService.ConvertFormation(request.Formation), sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
