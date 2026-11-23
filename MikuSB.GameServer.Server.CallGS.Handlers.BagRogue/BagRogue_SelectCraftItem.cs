using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_SelectCraftItem")]
public class BagRogue_SelectCraftItem : CallGSHandler<BagRogueCraftSelectParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueCraftSelectParam request)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueId"] = request.Index }));
	}
}
