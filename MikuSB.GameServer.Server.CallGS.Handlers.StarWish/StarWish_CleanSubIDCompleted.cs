using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

[CallGSApi("StarWish_CleanSubIDCompleted")]
public class StarWish_CleanSubIDCompleted : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		try
		{
			uint id = StarWishQuests.ReadId(JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param)?["nID"]);
			if (id != 0)
			{
				StarWishAttr.Set(player, StarWishQuests.ProgressSid(id), 0u, sync);
				DatabaseHelper.SaveDatabaseType(player.Data);
			}
		}
		catch
		{
		}
		await CallGSRouter.SendScript(connection, "StarWish_CleanSubIDCompleted", "{}", sync);
	}
}
