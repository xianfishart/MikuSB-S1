using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.GachaTry;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GachaTry;

[CallGSApi("GacheTry_GetAlreadyPassedAward")]
public class GacheTry_GetAlreadyPassedAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject response = GachaTryService.BuildPassedAwards(actId: ((JsonNode.Parse(param) as JsonObject)?["nActId"]?.GetValue<int>()).GetValueOrDefault(), player: connection.Player);
		await CallGSRouter.SendScript(connection, "GacheTry_GetAlreadyPassedAward", response.ToJsonString());
	}
}
