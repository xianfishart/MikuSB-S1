using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Rogue3D;

[CallGSApi("Rogue3D_RoomSettlement")]
public class Rogue3D_RoomSettlement : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await CallGSRouter.SendScript(connection, "Rogue3D_RoomSettlement", "{}");
	}
}
