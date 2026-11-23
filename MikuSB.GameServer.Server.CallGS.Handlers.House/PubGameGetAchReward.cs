using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("PubGameGetAchReward")]
public class PubGameGetAchReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int achId = PubAttr.Num(root["nAchId"]);
		if (achId == 0)
		{
			achId = PubAttr.Num(root["nId"]);
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = ((achId <= 0) ? (await HouseAchievementClaim.ClaimAllAsync(connection, HouseAchievementClaim.HouseAchievementIds(), sync)) : (await HouseAchievementClaim.ClaimAsync(connection, achId, sync)));
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "PubGameGetAchReward",
			["tbReward"] = rewards
		};
		await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString(), sync);
	}
}
