using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Chess;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("Chess_EnterLevel")]
public class Chess_EnterLevel : CallGSHandler<ChessEnterLevelParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChessEnterLevelParam request)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nSeed"] = ChessActivityService.NewSeed() }));
	}
}
