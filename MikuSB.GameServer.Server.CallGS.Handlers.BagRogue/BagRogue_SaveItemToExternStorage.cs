using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_SaveItemToExternStorage")]
public class BagRogue_SaveItemToExternStorage : CallGSHandler<BagRogueSaveExternParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSaveExternParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint value = player.Attributes.GetValue(142u, 7u);
		if (value != 0)
		{
			PlayerAttr attr = player.Attributes.Set(142u, 7u, value - 1);
			player.Attributes.SyncTo(sync, attr);
			DatabaseHelper.SaveDatabaseType(player.Data);
		}
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
