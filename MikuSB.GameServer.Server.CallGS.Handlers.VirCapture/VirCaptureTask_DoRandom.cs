using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCaptureTask_DoRandom")]
public class VirCaptureTask_DoRandom : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		return CallGSRouter.SendScript(connection, "VirCaptureTask_DoRandom", "{}");
	}
}
