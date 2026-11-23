using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_UseDice")]
public class RichManLoveLogic_UseDice : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject()));
	}
}
