using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("LogHouseStoryFinish")]
public class LogHouseStoryFinish : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root != null)
		{
			int storyType = HouseJson.NumField(root, "StoryType");
			string storyIdText = root["StoryId"]?.ToString() ?? "";
			int storyId = (int.TryParse(storyIdText, out var parsed) ? parsed : 0);
			NtfSyncPlayer sync = new NtfSyncPlayer();
			JsonArray reward = new JsonArray();
			if (storyType == 3 && storyId > 0)
			{
				reward = await HouseLoveStoryRewards.ClaimByStory(connection, storyId, sync);
			}
			JsonObject response = new JsonObject
			{
				["FuncName"] = "LogHouseStoryFinish",
				["bSuccess"] = true,
				["tbReward"] = reward
			};
			await CallGSRouter.SendScript(connection, "House_Request", response.ToJsonString(), sync);
		}
	}
}
