using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GetGirlMassageHardLevelReward")]
public class GetGirlMassageHardLevelReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int girlId = HouseJson.NumField(root, "GirlId");
		int hardLevel = HouseJson.NumField(root, "HardLevel");
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "GetGirlMassageHardLevelReward",
			["tbReward"] = new JsonArray()
		};
		NtfSyncPlayer sync = new NtfSyncPlayer();
		List<uint>? reward = HouseMassageHelper.GirlReward(girlId, hardLevel);
		if (reward != null && reward.Count != 0)
		{
			// 2026-11-13 改（修复记录第 272 节）：客户端 GetGirlMassageHardLevelRewardState 读的是
			// 11000+少女*50+38 的「第 hardLevel 位」（1/2/3），旧实现按 hardLevel-1 写，
			// 就算领了客户端也永远认不出来。
			bool granted = await HouseMassageHelper.GrantStarRewardAsync(connection, sync, girlId, hardLevel, force: false);
			if (granted)
			{
				JsonNode[] array = new JsonNode[reward.Count];
				for (int i = 0; i < reward.Count; i++)
				{
					array[i] = JsonValue.Create(reward[i]);
				}
				rsp["tbReward"] = new JsonArray(array);
			}
		}
		// 顺带补齐 20702「完成一次神经调理」与老存档的星级/经验（幂等，不重复发奖）。
		await HouseMassageAttr.MarkAchievementIfNeededAsync(connection, sync);
		await HouseMassageHelper.RepairAsync(connection, sync);
		await CallGSRouter.SendScript(connection, "House_Request", rsp.ToJsonString(), sync);
	}
}
