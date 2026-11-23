using System.Text.Json;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.BossPvp;

namespace MikuSB.GameServer.Server.CallGS.Handlers.BossPvp;

[CallGSApi("BossPvpLogic_EnterLevel")]
public class BossPvpLogic_EnterLevel : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		object response = BossPvpService.HandleEnterLevel(connection.Player, param);
		await CallGSRouter.SendScript(connection, "BossPvpLogic_EnterLevel", JsonSerializer.Serialize(response));
	}
}
