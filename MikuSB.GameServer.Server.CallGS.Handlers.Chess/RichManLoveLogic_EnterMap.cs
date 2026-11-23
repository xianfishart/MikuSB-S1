using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_EnterMap")]
public class RichManLoveLogic_EnterMap : CallGSHandler<RichmanEnterMapParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanEnterMapParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint num = ChessActivityService.OpenSession(player, 3u, request.MapId, sync, out var first);
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nId"] = 3u,
			["nMapId"] = request.MapId,
			["nSeed"] = num,
			["First"] = (first ? 1 : 0)
		}, sync));
	}
}
