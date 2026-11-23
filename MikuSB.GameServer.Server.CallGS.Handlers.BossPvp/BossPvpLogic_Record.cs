using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_Record")]
public class BossPvpLogic_Record : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(object, NtfSyncPlayer) tuple = BossPvpService.HandleRecord(connection.Player, param);
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Item2, connection: connection, api: "BossPvpLogic_Record", arg: JsonSerializer.Serialize(response));
	}
}
