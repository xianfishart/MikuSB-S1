using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DarkZone;

[CallGSApi("DarkZone_TrackingMission")]
public class DarkZone_TrackingMission : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "DarkZone_TrackingMission", "{}");
	}
}
