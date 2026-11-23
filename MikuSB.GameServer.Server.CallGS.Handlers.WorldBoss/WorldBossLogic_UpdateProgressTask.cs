using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

[CallGSApi("WorldBossLogic_UpdateProgressTask")]
public class WorldBossLogic_UpdateProgressTask : CallGSHandler<WorldBossUpdateProgressParam>
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, WorldBossUpdateProgressParam request)
	{
		Logger.GetByClassName().Debug($"WorldBoss UpdateProgressTask act={request.ActivityId} for uid={context.Connection.Player?.Uid ?? 0}.");
		return Task.FromResult(CallGSResult.Ok());
	}
}
