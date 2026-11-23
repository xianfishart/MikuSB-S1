using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("DarkZone_RefreshMission")]
public class DarkZone_RefreshMission : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "DarkZone_RefreshMission", "{}");
	}
}
