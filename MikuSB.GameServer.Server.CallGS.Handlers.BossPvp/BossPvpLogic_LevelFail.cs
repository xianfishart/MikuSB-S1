using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_LevelFail")]
public class BossPvpLogic_LevelFail : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(JsonNode, NtfSyncPlayer) tuple = BossPvpService.HandleFail(param: JsonNode.Parse(param), player: connection.Player);
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Item2, connection: connection, api: "BossPvpLogic_LevelFail", arg: response.ToJsonString());
	}
}
