using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Repression_ChangeFormation")]
public class Repression_ChangeFormation : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player != null)
		{
			try
			{
				JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
				JsonArray formation = default;
				int num;
				if (jsonNode is JsonObject req)
				{
					jsonNode = req["tbFormation"];
					formation = jsonNode as JsonArray;
					num = ((formation != null) ? 1 : 0);
				}
				else
				{
					num = 0;
				}
				if (num != 0)
				{
					NtfSyncPlayer sync = new NtfSyncPlayer();
					RepressionState.RecordFormation(player, formation, sync);
					DatabaseHelper.SaveDatabaseType(player.Data);
					await CallGSRouter.SendScript(connection, "Repression_ChangeFormation", "{}", sync);
					return;
				}
			}
			catch
			{
			}
		}
		await CallGSRouter.SendScript(connection, "Repression_ChangeFormation", "{}");
	}
}
