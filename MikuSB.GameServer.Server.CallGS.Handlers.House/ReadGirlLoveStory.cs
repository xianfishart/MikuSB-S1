using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Database;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("ReadGirlLoveStory")]
public class ReadGirlLoveStory : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param);
		if (root == null)
		{
			return;
		}
		int girlId = HouseJson.NumField(root, "GirlId");
		int index = HouseJson.NumField(root, "Index");
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray reward = new JsonArray();
		if (girlId > 0 && index >= 0 && index <= 30)
		{
			reward = await HouseLoveStoryRewards.Claim(connection, girlId, index, sync);
			if (reward.Count > 0)
			{
				uint sid = (uint)(girlId * 50 + 2);
				uint prev = HouseAttr.Read(connection.Player, sid);
				await HouseAttr.SetAsync(connection, sid, prev | (uint)(1 << index), sync, deleteIfZero: false, sendImmediate: true);
			}
		}
		JsonObject rsp = new JsonObject
		{
			["GirlId"] = girlId,
			["Index"] = index,
			["tbReward"] = reward,
			["FuncName"] = "ReadGirlLoveStorySuccess"
		};
		DatabaseHelper.SaveDatabaseType(connection.Player.Data);
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
