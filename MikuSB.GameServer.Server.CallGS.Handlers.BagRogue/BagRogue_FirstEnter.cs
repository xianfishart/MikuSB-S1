using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_FirstEnter")]
public class BagRogue_FirstEnter : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bSet"] = false }));
		}
		if (player.Attributes.GetValue(142u, 8u) != 0)
		{
			return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bSet"] = false }));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Set(142u, 8u, 1u);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["bSet"] = true }, sync));
	}
}
