using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

[CallGSApi("FarmWarLogic_ActiveGrowNode")]
public class FarmWarLogic_ActiveGrowNode : CallGSHandler<FarmWarGrowParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
	{
		return Task.FromResult(FarmWarGrowOps.Apply(context.Connection.Player, request, active: true, "FarmWarLogic_ActiveGrowNode"));
	}
}
