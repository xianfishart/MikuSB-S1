using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_RemoveExternStorageItem")]
public class BagRogue_RemoveExternStorageItem : CallGSHandler<BagRogueSellParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, BagRogueSellParam request)
	{
		return Task.FromResult(CallGSResult.Ok(new JsonObject { ["nUniqueId"] = request.UniqueId }));
	}
}
