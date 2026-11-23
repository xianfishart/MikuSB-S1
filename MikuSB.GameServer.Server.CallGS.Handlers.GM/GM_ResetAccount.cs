using System.Threading.Tasks;
using MikuSB.Database.Account;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GM;

[CallGSApi("GM_ResetAccount")]
public class GM_ResetAccount : ICallGSHandler
{
	public Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return Task.CompletedTask;
		}
		if (!AccountData.HasPerm(new PermEnum[1] { PermEnum.Admin }, player.Uid))
		{
			return Task.CompletedTask;
		}
		PendingAccountReset.Request(player.Uid);
		return Task.CompletedTask;
	}
}
