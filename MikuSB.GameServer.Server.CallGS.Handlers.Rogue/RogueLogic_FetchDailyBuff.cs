using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue;

[CallGSApi("RogueLogic_FetchDailyBuff")]
public class RogueLogic_FetchDailyBuff : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject
		{
			["tbDailyBuffRole"] = new JsonArray(),
			["tbDailyBuff"] = new JsonArray()
		}));
	}
}
