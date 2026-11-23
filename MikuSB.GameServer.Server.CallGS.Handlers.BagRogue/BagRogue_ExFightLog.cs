using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BagRogue;

[CallGSApi("BagRogue_ExFightLog")]
public class BagRogue_ExFightLog : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		return Task.FromResult(CallGSResult.NoResponse());
	}
}
