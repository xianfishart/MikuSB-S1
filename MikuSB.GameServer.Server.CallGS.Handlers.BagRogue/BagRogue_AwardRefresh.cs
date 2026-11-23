using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_AwardRefresh")]
public class BagRogue_AwardRefresh : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.Add(142u, 13u, 1u);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
