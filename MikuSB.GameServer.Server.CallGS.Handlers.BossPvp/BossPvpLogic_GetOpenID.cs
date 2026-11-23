using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.BossPvp;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_GetOpenID")]
public class BossPvpLogic_GetOpenID : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		(object Response, NtfSyncPlayer Sync) tuple = await BossPvpService.HandleGetOpenIdAsync(connection.Player);
		var (response, _) = tuple;
		await CallGSRouter.SendScript(extra: tuple.Sync, connection: connection, api: "BossPvpLogic_GetOpenID", arg: JsonSerializer.Serialize(response));
	}
}
