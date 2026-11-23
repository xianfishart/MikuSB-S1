using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

[CallGSApi("DonoDuelArena_LevelSettlement")]
public class DonoDuelArena_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		(JsonObject Response, NtfSyncPlayer Sync) tuple = await DonoDuelArenaSettlement.SettleAsync(player, request);
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Sync, connection: connection, api: "DonoDuelArena_LevelSettlement", arg: response.ToJsonString());
	}
}
