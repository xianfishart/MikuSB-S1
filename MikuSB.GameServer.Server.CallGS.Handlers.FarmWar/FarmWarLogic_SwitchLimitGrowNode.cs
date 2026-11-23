using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

[CallGSApi("FarmWarLogic_SwitchLimitGrowNode")]
public class FarmWarLogic_SwitchLimitGrowNode : CallGSHandler<FarmWarGrowParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
	{
		JsonObject jsonObject = new JsonObject { ["nId"] = request.Id };
		return Task.FromResult(CallGSResult.Ok(jsonObject.ToJsonString(), null, "FarmWarLogic_SwitchLimitGrowNode"));
	}
}
