using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("ChessLogic_EnterMap")]
public class ChessLogic_EnterMap : CallGSHandler<ChessEnterMapParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, ChessEnterMapParam request)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["nId"] = request.Id,
			["nMapId"] = request.MapId
		}));
	}
}
