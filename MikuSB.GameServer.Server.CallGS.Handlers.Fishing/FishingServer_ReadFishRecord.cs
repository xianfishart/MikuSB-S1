using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

[CallGSApi("FishingServer_ReadFishRecord")]
public class FishingServer_ReadFishRecord : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, "FishingServer_ReadFishRecord", "{}");
	}
}
