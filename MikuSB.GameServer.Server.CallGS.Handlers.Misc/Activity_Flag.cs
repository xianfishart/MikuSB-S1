using System.Threading.Tasks;
using MikuSB.GameServer.Game.Inventory;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("Activity_Flag")]
public class Activity_Flag : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		await GrantAllItems.GrantMissing(connection.Player);
	}
}
