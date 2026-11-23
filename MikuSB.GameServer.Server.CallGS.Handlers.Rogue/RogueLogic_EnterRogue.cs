using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Game.Rogue;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_EnterRogue")]
public class RogueLogic_EnterRogue : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		(RogueActivityExcel, JsonObject)? tuple = RogueService.EnsureStarted(player, sync);
		if (tuple.HasValue)
		{
			(RogueActivityExcel, JsonObject) valueOrDefault = tuple.GetValueOrDefault();
			if (true)
			{
				RogueService.SeedOpenStory(player, valueOrDefault.Item1, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
				return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
			}
		}
		return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
	}
}
