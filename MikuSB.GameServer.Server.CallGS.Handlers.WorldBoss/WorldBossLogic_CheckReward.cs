using System.Threading.Tasks;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

[CallGSApi("WorldBossLogic_CheckReward")]
public class WorldBossLogic_CheckReward : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		Logger.GetByClassName().Debug($"WorldBoss CheckReward (no response) for uid={context.Connection.Player?.Uid ?? 0}.");
		return Task.FromResult(CallGSResult.NoResponse());
	}
}
