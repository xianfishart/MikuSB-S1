using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_ResetRogue")]
public class RogueLogic_ResetRogue : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		RogueService.Reset(player, sync);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
