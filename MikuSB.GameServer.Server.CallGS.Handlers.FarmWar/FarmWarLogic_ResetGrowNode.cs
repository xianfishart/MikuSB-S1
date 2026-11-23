using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

[CallGSApi("FarmWarLogic_ResetGrowNode")]
public class FarmWarLogic_ResetGrowNode : CallGSHandler<FarmWarGrowParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
	{
		return Task.FromResult(FarmWarGrowOps.Apply(context.Connection.Player, request, active: false, "FarmWarLogic_ResetGrowNode"));
	}
}
