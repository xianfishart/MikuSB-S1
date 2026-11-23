using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("RichManLoveLogic_EnterMapLog")]
public class RichManLoveLogic_EnterMapLog : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		return Task.FromResult(CallGSResult.NoResponse());
	}
}
