using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

/// <summary>
/// 心动水弹：领取单级奖励（2026-11-12 重写，修复记录第 258 节）。
/// 客户端请求：{"Level":n,"FuncName":"GetWaterGunLevelReward"}（运行时抓包实测）。
/// 规则（与「海边扔物 ThrowGameGetLevelReward」同款）：
///   · 只能领「已达等级」及以下，超过当前等级一律不发；
///   · 已领过的不再发（位图 15013+ 去重），避免重登后重复领；
///   · 领成功才返回奖励，失败返回空数组 + bReceived=false，客户端不会把该级标成已领。
/// </summary>
[HouseFunc("GetWaterGunLevelReward")]
public class GetWaterGunLevelReward : IHouseFuncHandler
{
	public async Task Handle(Connection connection, string param)
	{
		JsonObject root = HouseJson.ParseObject(param) ?? new JsonObject();
		int level = HouseJson.NumField(root, "Level");
		if (level <= 0)
		{
			level = HouseJson.NumField(root, "nLevel");
		}
		PlayerInstance player = connection.Player;
		NtfSyncPlayer sync = new NtfSyncPlayer();
		int curLevel = WaterGunHelper.ReadLevel(player);
		bool got = level > 0 && WaterGunHelper.IsGot(player, level);
		JsonArray flat = new JsonArray();
		if (level > 0 && level <= curLevel && !got)
		{
			List<uint> reward = await WaterGunHelper.GrantAsync(connection, level, sync);
			if (reward != null)
			{
				flat = WaterGunHelper.ToJson(reward);
				got = true;
			}
		}
		bool canGet = level > 0 && level <= curLevel && !WaterGunHelper.IsGot(player, level);
		JsonObject rsp = new JsonObject
		{
			["FuncName"] = "GetWaterGunLevelRewardRsp",
			["Level"] = level,
			["nLevel"] = level,
			["bReceived"] = got,
			["bCanGet"] = canGet,
			["tbReward"] = flat,
			["tbRewards"] = flat.DeepClone(),
			["nCurLevel"] = curLevel,
			["nCurPoint"] = WaterGunHelper.ReadPoint(player),
			["nNeedExp"] = WaterGunHelper.NeedExp(curLevel)
		};
		await CallGSRouter.SendScript(connection, "House_Request", HouseRequestScript.Success(rsp), sync);
	}
}
