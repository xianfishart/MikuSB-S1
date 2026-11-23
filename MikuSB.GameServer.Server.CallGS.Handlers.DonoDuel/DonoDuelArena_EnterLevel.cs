using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DonoDuel;

[CallGSApi("DonoDuelArena_EnterLevel")]
public class DonoDuelArena_EnterLevel : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject request = (JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject) ?? new JsonObject();
		if (DonoDuelFormation.ReadUint(request["nID"]) == 0)
		{
			await CallGSRouter.SendScript(connection, "DonoDuelArena_EnterLevel", "{\"sErr\":\"error.BadParam\"}");
			return;
		}
		await CallGSRouter.SendScript(connection, "DonoDuelArena_EnterLevel", new JsonObject { ["nSeed"] = Random.Shared.Next(1, 1000000000) }.ToJsonString());
	}
}
