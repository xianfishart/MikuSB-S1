using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.CallGS.Handlers.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("HouseOverviewGetReward")]
public class HouseOverviewGetReward : IHouseFuncHandler
{
	private static Dictionary<int, int>? _areaToTask;

	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		PlayerInstance player = connection.Player;
		if (player == null)
		{
			return;
		}
		int areaId = 0;
		try
		{
			areaId = root["nAreaID"]?.GetValue<int>() ?? 0;
		}
		catch
		{
		}
		int taskId = GetTaskId(areaId);
		if (taskId <= 0)
		{
			await CallGSRouter.SendScript(connection, "House_Request", "{\"sErr\":\"ui.TxtNotOpen\"}");
			return;
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<JsonArray> groups = await TaskProgressService.Claim(connection, taskId, sync);
		TaskProgressService.Save(player);
		JsonArray rewards = new JsonArray();
		foreach (JsonArray group in groups)
		{
			foreach (JsonNode item in group)
			{
				rewards.Add(item?.DeepClone());
			}
		}
		JsonObject resp = new JsonObject();
		foreach (var (key, jsonNode2) in root)
		{
			resp[key] = jsonNode2?.DeepClone();
		}
		resp["tbReward"] = rewards;
		await CallGSRouter.SendScript(connection, "House_Request", resp.ToJsonString(), sync);
	}

	private static int GetTaskId(int areaId)
	{
		if (areaId <= 0)
		{
			return 0;
		}
		if (_areaToTask == null)
		{
			_areaToTask = LoadMap();
		}
		int value;
		return _areaToTask.TryGetValue(areaId, out value) ? value : 0;
	}

	private static Dictionary<int, int> LoadMap()
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		try
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "house", "dorm_overview.json");
			if (!File.Exists(path))
			{
				return dictionary;
			}
			JsonArray jsonArray = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
			if (jsonArray == null)
			{
				return dictionary;
			}
			foreach (JsonNode item in jsonArray)
			{
				if (item is JsonObject jsonObject)
				{
					int num = Int(jsonObject["AreaID"]);
					int num2 = Int(jsonObject["TaskID"]);
					if (num > 0 && num2 > 0)
					{
						dictionary[num] = num2;
					}
				}
			}
		}
		catch
		{
		}
		return dictionary;
	}

	private static int Int(JsonNode? node)
	{
		int result;
		return int.TryParse(node?.ToString(), out result) ? result : 0;
	}
}
