using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("AlienDefense_SetPalFlag")]
public class AlienDefense_SetPalFlag : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "AlienDefense_SetPalFlag", "{}");
	}
}
