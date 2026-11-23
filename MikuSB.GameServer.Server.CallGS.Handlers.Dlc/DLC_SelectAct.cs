using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("DLC_SelectAct")]
public class DLC_SelectAct : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		uint eraId = 0u;
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (jsonNode is JsonObject req)
			{
				JsonNode node = req["nId"] ?? req["nID"] ?? req["id"];
				if (node is JsonValue value)
				{
					int i;
					double d;
					if (value.TryGetValue<uint>(out var u))
					{
						eraId = u;
					}
					else if (value.TryGetValue<int>(out i) && i > 0)
					{
						eraId = (uint)i;
					}
					else if (value.TryGetValue<double>(out d) && d > 0.0)
					{
						eraId = (uint)d;
					}
				}
			}
		}
		catch
		{
		}
		if (eraId != 0 && GameData.DlcActivityData.ContainsKey(eraId))
		{
			NtfSyncPlayer sync = new NtfSyncPlayer();
			DlcEraState.SetEra(player, eraId, sync);
			DatabaseHelper.SaveDatabaseType(player.Data);
			await connection.SendPacket(1004, sync, 0);
		}
	}
}
