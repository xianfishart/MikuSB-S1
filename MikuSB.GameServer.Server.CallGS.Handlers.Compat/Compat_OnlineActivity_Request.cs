namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("OnlineActivity_Request")]
public class Compat_OnlineActivity_Request : EmptyAck
{
	protected override string Api => "OnlineActivity_Request";
}
