using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 心动水弹：领取单条任务奖励（2026-11-12 重写，修复记录第 261 节）。
/// 客户端报文：{"FuncName":"GetWaterGunAchReward","AchId":20101}（运行时抓包实测）。
/// 旧实现查的是 Resources/achievement/achievement.json 的 Gid=2（账号等级成就），
/// 与客户端真正使用的 activity_quest.json（Gid=7，任务成就）不是同一套数据，
/// 所以即使进度满了也领不出东西，而且不回同步。
/// 现在统一走 HouseAchievementClaim：读 activity_quest.json、置 Gid=7 属性最低位、按 Rewards 发奖。
/// </summary>
[HouseFunc("GetWaterGunAchReward")]
public class GetWaterGunAchReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int achId = HouseJson.NumField(root, "AchId");
		if (achId <= 0)
		{
			achId = HouseJson.NumField(root, "nAchId");
		}
		NtfSyncPlayer sync = new NtfSyncPlayer();
		JsonArray rewards = new JsonArray();
		if (achId > 0)
		{
			foreach (JsonNode? item in await HouseAchievementClaim.ClaimAsync(connection, achId, sync))
			{
				rewards.Add(item?.DeepClone());
			}
		}
		// 2026-11-12 追加（修复记录第 263 节）：FuncName 必须带 Rsp 后缀。
		// 客户端 HouseMessageHandle 的回调名是 GetWaterGunAchRewardRsp，
		// 回包名不匹配时既不关 connection 遮罩、也不展示奖励（界面像卡住），
		// 与 GetWaterGunLevelRewardRsp / GetWaterGunAllLevelRewardRsp 的写法保持一致。
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "GetWaterGunAchRewardRsp",
			["AchId"] = achId,
			["nAchId"] = achId,
			["bReceived"] = rewards.Count > 0,
			["tbReward"] = rewards,
			["tbRewards"] = rewards.DeepClone()
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
