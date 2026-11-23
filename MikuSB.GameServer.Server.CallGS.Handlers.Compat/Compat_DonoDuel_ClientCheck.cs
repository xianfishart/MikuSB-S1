namespace MikuSB.GameServer.Server.CallGS.Handlers.Compat;

[CallGSApi("DonoDuel_ClientCheck")]
public class Compat_DonoDuel_ClientCheck : EmptyAck
{
	protected override string Api => "DonoDuel_ClientCheck";
}
