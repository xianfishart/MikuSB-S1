using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_SyncGuideLevelPassData")]
public class Chapter_SyncGuideLevelPassData : ICallGSHandler
{
	private const uint PassGroupId = 22u;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		try
		{
			if (!(JsonNode.Parse(param)?["tbData"] is JsonArray data))
			{
				return;
			}
			NtfSyncPlayer sync = new NtfSyncPlayer();
			foreach (JsonNode node in data)
			{
				JsonArray row = node as JsonArray;
				if (row != null && row.Count >= 2 && uint.TryParse(row[0]?.ToString(), out var levelId) && levelId != 0 && uint.TryParse(row[1]?.ToString(), out var passTime) && passTime != 0)
				{
					player.SetAttr(22u, levelId, passTime);
					sync.Custom[player.ToPackedAttrKey(22u, levelId)] = passTime;
					sync.Custom[player.ToShiftedAttrKey(22u, levelId)] = passTime;
				}
			}
			DatabaseHelper.SaveDatabaseType(player.Data);
			if (sync.Custom.Count > 0)
			{
				await connection.SendPacket(1004, sync, 0);
			}
		}
		catch
		{
		}
	}
}
