using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("ZoneTime_ReqTime")]
public class ZoneTime_ReqTime : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		string arg = "{\"nTime1\":false,\"nTime2\":false}";
		await CallGSRouter.SendScript(connection, "ZoneTime_ChangeTime", arg);
	}
}
