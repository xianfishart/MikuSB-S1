using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTask_DoCheckCostTask")]
public class VirCaptureTask_DoCheckCostTask : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureTask_DoCheckCostTask", "{}");
	}
}
