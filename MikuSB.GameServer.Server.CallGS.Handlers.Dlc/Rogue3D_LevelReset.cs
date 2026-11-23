using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MikuSB.GameServer.Server.CallGS.Handlers.DLC;

[CallGSApi("Rogue3D_LevelReset")]
public class Rogue3D_LevelReset : ICallGSHandler
{
	public async Task Handle(Connection connection, string param, ushort seqNo)
	{
		JsonObject rsp = new JsonObject();
		try
		{
			JsonNode jsonNode = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param);
			if (jsonNode is JsonObject req)
			{
				foreach (KeyValuePair<string, JsonNode> kv in req)
				{
					rsp[kv.Key] = kv.Value?.DeepClone();
				}
			}
		}
		catch
		{
		}
		JsonObject jsonObject = rsp;
		if (jsonObject["tbAwardList"] == null)
		{
			jsonObject["tbAwardList"] = new JsonArray();
		}
		await CallGSRouter.SendScript(connection, "Rogue3D_LevelReset", rsp.ToJsonString());
	}
}
