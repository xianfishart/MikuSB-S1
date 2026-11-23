using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Daily;

[CallGSApi("Daily_SetSelectSuit")]
public class Daily_SetSelectSuit : ICallGSHandler
{
	private const uint GroupId = 102u;

	private const uint SuitMask = 65535u;

	private const int SuitShift = 16;

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint levelId = ReadUint(request["Type"]);
		uint suit = ReadUint(request["Suit"]);
		if (levelId == 0 || suit == 0)
		{
			await CallGSRouter.SendScript(connection, "Daily_SetSelectSuit", "{}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		PlayerAttr attr = player.Attributes.GetOrCreate(102u, levelId);
		attr.Val = (attr.Val & 0xFFFF) | ((suit & 0xFFFF) << 16);
		player.Attributes.SyncTo(sync, attr);
		DatabaseHelper.SaveDatabaseType(player.Data);
		await CallGSRouter.SendScript(connection, "Daily_SetSelectSuit", new JsonObject { ["SuitId"] = suit }.ToJsonString(), sync);
	}

	private static uint ReadUint(JsonNode? node)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<uint>(out var value))
		{
			return value;
		}
		uint result;
		return uint.TryParse(node?.ToString(), out result) ? result : 0u;
	}
}
