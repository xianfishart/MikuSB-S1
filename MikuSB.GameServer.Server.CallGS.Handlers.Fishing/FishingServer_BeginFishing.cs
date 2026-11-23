using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Fishing;

[CallGSApi("FishingServer_BeginFishing")]
public class FishingServer_BeginFishing : ICallGSHandler
{
	internal static readonly ConcurrentDictionary<int, (uint NodeId, uint FishId)> Pending = new ConcurrentDictionary<int, (uint, uint)>();

	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		PlayerInstance player = connection.Player;
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		uint nodeId = ReadUint(request["nNodeID"]);
		if (nodeId == 0 || !GameData.FishingNodeData.TryGetValue(nodeId, out FishingNodeExcel node))
		{
			await CallGSRouter.SendScript(connection, "FishingServer_BeginFishing", "{\"sError\":\"error.BadParam\"}");
			return;
		}
		uint fishId = FishingRules.PickFish(player, node);
		if (fishId == 0)
		{
			await CallGSRouter.SendScript(connection, "FishingServer_BeginFishing", "{\"sError\":\"ui.TxtNotOpen\"}");
			return;
		}
		Pending[player.Uid] = (nodeId, fishId);
		await CallGSRouter.SendScript(connection, "FishingServer_BeginFishing", new JsonObject { ["nfishid"] = fishId }.ToJsonString());
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
