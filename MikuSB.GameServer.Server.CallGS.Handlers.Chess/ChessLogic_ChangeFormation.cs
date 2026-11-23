using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("ChessLogic_ChangeFormation")]
public class ChessLogic_ChangeFormation : CallGSHandler<ChessFormationParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChessFormationParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		string value = request.Formation?.ToJsonString() ?? "[]";
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerStrAttr attr = player.Attributes.SetString(12u, 51u, value);
		player.Attributes.SyncTo(sync, attr);
		return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
	}
}
