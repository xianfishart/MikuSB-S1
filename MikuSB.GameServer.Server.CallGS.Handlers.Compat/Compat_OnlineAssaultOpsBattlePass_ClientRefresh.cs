namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("OnlineAssaultOpsBattlePass_ClientRefresh")]
public class Compat_OnlineAssaultOpsBattlePass_ClientRefresh : EmptyAck
{
	protected override string Api => "OnlineAssaultOpsBattlePass_ClientRefresh";
}
