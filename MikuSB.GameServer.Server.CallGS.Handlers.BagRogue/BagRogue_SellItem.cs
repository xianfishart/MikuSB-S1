using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_SellItem")]
public class BagRogue_SellItem : CallGSHandler<BagRogueSellParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSellParam request)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueId"] = request.UniqueId }));
	}
}
