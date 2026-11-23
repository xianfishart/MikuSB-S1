using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 心动水弹：一键领取（2026-11-12 重写，修复记录第 258 节）。
/// 客户端请求：{"FuncName":"GetWaterGunAllLevelReward"}（无附加参数）。
///
/// 旧实现的三个错误（就是「奖励发放不正常」的根源）：
///   1) 不判断当前等级，1 级也能把 15 级奖励全领走；
///   2) 用 Gid=203 / Sid=20000+level 当领取标记，和单级领取用的 101/15013 位图不是一套；
///   3) 每次都把 15 级全部「发一遍并标已领」，所以重登后再点也照样全发。
/// 现在的规则：
///   · 只遍历 1..当前等级；
///   · 已领过的跳过（15013 位图持久化，重登有效）；
///   · 一次点击只发「已达等级里还没领的」，全领过就返回空列表（客户端按钮自然就灰/无反应）。
/// </summary>
[HouseFunc("GetWaterGunAllLevelReward")]
public class GetWaterGunAllLevelReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int curLevel = WaterGunHelper.ReadLevel(player);
		JsonArray rewards = new JsonArray();
		JsonArray levels = new JsonArray();
		for (int level = 1; level <= curLevel; level++)
		{
			if (WaterGunHelper.IsGot(player, level))
			{
				continue;
			}
			List<uint> reward = await WaterGunHelper.GrantAsync(connection, level, sync);
			if (reward == null)
			{
				continue;
			}
			rewards.Add(WaterGunHelper.ToJson(reward));
			levels.Add(JsonValue.Create(level));
		}
		JsonObject extra = new JsonObject
		{
			["FuncName"] = "GetWaterGunAllLevelRewardRsp",
			["tbRewards"] = rewards,
			["tbReward"] = rewards.DeepClone(),
			["tbRewardList"] = rewards.DeepClone(),
			["tbLevels"] = levels,
			["tbLevel"] = levels.DeepClone(),
			["nCurLevel"] = curLevel,
			["nCurPoint"] = WaterGunHelper.ReadPoint(player),
			["nNeedExp"] = WaterGunHelper.NeedExp(curLevel)
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(extra), sync);
	}
}
