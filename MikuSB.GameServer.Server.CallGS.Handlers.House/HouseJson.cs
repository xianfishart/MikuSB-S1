using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseJson
{
	internal static JsonObject? ParseObject(string json)
	{
		try
		{
			return JsonNode.Parse(json) as JsonObject;
		}
		catch
		{
			return null;
		}
	}

	internal static int NumField(JsonObject? obj, string name)
	{
		return ToInt(obj?[name]);
	}

	internal static int ToInt(JsonNode? node)
	{
		if (node == null)
		{
			return 0;
		}
		try
		{
			if (node is JsonValue jsonValue)
			{
				if (jsonValue.TryGetValue<int>(out var value))
				{
					return value;
				}
				if (jsonValue.TryGetValue<uint>(out var value2))
				{
					return (int)value2;
				}
				if (jsonValue.TryGetValue<long>(out var value3))
				{
					return (int)value3;
				}
				if (jsonValue.TryGetValue<string>(out string value4) && int.TryParse(value4, out var result))
				{
					return result;
				}
			}
		}
		catch
		{
		}
		return 0;
	}
}
