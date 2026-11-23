using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_LevelSettlement")]
public class BossPvpLogic_LevelSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(JsonNode, NtfSyncPlayer) tuple = BossPvpService.HandleSettlement(param: JsonNode.Parse(param), player: connection.Player);
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Item2, connection: connection, api: "BossPvpLogic_LevelSettlement", arg: response.ToJsonString());
	}
}
