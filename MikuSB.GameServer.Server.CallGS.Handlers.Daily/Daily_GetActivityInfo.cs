using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

[CallGSApi("Daily_GetActivityInfo")]
public class Daily_GetActivityInfo : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Daily_GetActivityInfo", "{}");
	}
}
