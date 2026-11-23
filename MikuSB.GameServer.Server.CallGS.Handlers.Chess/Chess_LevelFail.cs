using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chess;

[CallGSApi("Chess_LevelFail")]
public class Chess_LevelFail : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		return Task.FromResult(CallGSResult.NoResponse());
	}
}
