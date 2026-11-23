using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.TargetShoot;

internal static class HouseShim
{
	public static JsonObject? Parse(string? param)
	{
		try
		{
			return string.IsNullOrWhiteSpace(param) ? new JsonObject() : (JsonNode.Parse(param) as JsonObject);
		}
		catch
		{
			return new JsonObject();
		}
	}
}
