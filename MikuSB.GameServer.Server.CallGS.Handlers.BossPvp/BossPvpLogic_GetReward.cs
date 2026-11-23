using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_GetReward")]
public class BossPvpLogic_GetReward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(object Response, NtfSyncPlayer Sync) tuple = await BossPvpService.HandleGetReward(connection.Player, param);
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Sync, connection: connection, api: "BossPvpLogic_GetReward", arg: JsonSerializer.Serialize(response));
	}
}
