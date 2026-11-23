using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("AlienDefense_GetQuestAward")]
public class AlienDefense_GetQuestAward : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "AlienDefense_GetQuestAward", "{}");
	}
}
