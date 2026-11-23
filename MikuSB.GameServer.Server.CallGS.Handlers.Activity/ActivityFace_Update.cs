using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

[CallGSApi("ActivityFace_Update")]
public class ActivityFace_Update : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "ActivityFace_Update", "{\"nFaceId\":0,\"nId\":0,\"nType\":0}");
	}
}
