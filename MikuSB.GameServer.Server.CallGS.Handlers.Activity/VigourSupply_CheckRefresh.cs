using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("VigourSupply_CheckRefresh")]
public class VigourSupply_CheckRefresh : CallGSHandler
{
	protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
	{
		PlayerInstance player = context.Connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		VigourSupplyService.Seed(player, sync);
		return Task.FromResult(CallGSResult.Ok(new JsonObject().ToJsonString(), sync, "VigourSupply_CheckRefresh"));
	}
}
