using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_LevelSelectAward")]
public class BagRogue_LevelSelectAward : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject()));
	}
}
