using System.Threading.Tasks;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

[CallGSApi("FarmWarLogic_ResetGrow")]
public class FarmWarLogic_ResetGrow : CallGSHandler<FarmWarGrowParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
	{
		NtfSyncPlayer sync = new NtfSyncPlayer();
		FarmWarActivityService.ResetAllSkills(context.Connection.Player, sync);
		return Task.FromResult(CallGSResult.Ok("{}", sync, "FarmWarLogic_ResetGrow"));
	}
}
