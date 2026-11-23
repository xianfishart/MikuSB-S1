using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("Activity_MarkTip")]
public class Activity_MarkTip : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		Logger.GetByClassName().Info($"Activity_MarkTip for uid={player.Uid}: {param}");
		return Task.FromResult(CallGSResult.Ok(new JsonObject().ToJsonString(), null, "Activity_MarkTip"));
	}
}
