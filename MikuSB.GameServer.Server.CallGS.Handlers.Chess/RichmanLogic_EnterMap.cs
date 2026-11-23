using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichmanLogic_EnterMap")]
public class RichmanLogic_EnterMap : CallGSHandler<RichmanEnterMapParam>
{
	private static readonly Logger Log = new Logger("RichmanLogic_EnterMap");

	protected override Task<CallGSResult> HandleAsync(CallGSContext context, RichmanEnterMapParam request)
	{
		PlayerInstance player = context.Connection.Player;
		if (player == null)
		{
			return Task.FromResult(CallGSResult.Error("error.BadParam"));
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		uint num = ChessActivityService.OpenSession(player, request.Id, request.MapId, sync, out var first);
		Log.Info($"uid={player.Uid} entered richman activity={request.Id} map={request.MapId} continue={request.Continue} seed={num} first={first}.");
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nId"] = request.Id,
			["nMapId"] = request.MapId,
			["nSeed"] = num,
			["First"] = (first ? 1 : 0)
		}, sync));
	}
}
